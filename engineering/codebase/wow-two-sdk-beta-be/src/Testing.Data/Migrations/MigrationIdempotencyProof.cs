using System.Data.Common;
using System.Globalization;
using Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.Parsers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Runs one set's scripts directly, history bypassed, to prove each idempotent (M59): Apply → Apply, Rollback → Rollback, Apply → Rollback → Apply.</summary>
/// <remarks>
///   - forward, each <c>Apply.sql</c> runs twice; the second run must succeed and leave the database exactly as the first left it
///   - backward, each <c>Rollback.sql</c> runs twice, newest first, under the same rule
///   - then every <c>Apply.sql</c> runs again, and the schema must match the first forward pass; rows may differ, such as fresh ids
///   - a transactional script runs in a transaction; a <c>-- @no-transaction</c> one runs statement by statement
///   - substitutes <c>{{schema}}</c> and the <c>{{schema:set}}</c> of a registered set, and creates the set's schema; touches no history, lock or marker
/// </remarks>
/// <param name="registry">The registered sets.</param>
/// <param name="parser">Splits a no-transaction script into statements.</param>
/// <param name="connect">Opens a connection to the test database.</param>
internal sealed class MigrationIdempotencyProof(
    IMigrationSetRegistry registry, ISqlStatementParser parser, Func<CancellationToken, ValueTask<DbConnection>> connect)
{
    private const string ApplyStep = "apply";
    private const string ApplyTwiceStep = "apply → apply";
    private const string RollbackStep = "rollback";
    private const string RollbackTwiceStep = "rollback → rollback";
    private const string ReapplyStep = "apply → rollback → apply";

    /// <summary>Proves one set, stopping at the first failed step.</summary>
    /// <param name="set">The set name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<MigrationIdempotencyReport> RunAsync(string set, CancellationToken ct)
    {
        var options = registry.GetOptions(set);
        var dialect = registry.GetDialect(set);
        if (registry.GetBroker(set).Read().IsFailure(out var error, out var raw))
            return new MigrationIdempotencyReport(set, [], new MigrationIdempotencyFailure("*", "read", error.Message));

        var migrations = raw
            .Select(migration => (Migration: migration, Match: MigrationConstants.FolderPattern().Match(migration.Name)))
            .Where(item => item.Match.Success)
            .OrderBy(item => int.Parse(item.Match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Select(item => new Script(
                $"{int.Parse(item.Match.Groups[1].Value, CultureInfo.InvariantCulture):D3}-{item.Match.Groups[2].Value}",
                Resolve(dialect, options, item.Migration.ApplySql),
                Resolve(dialect, options, item.Migration.RollbackSql),
                IsNoTransaction(item.Migration.ApplySql)))
            .ToList();

        var connection = await connect(ct).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await dialect.EnsureSchemaAsync(connection, null, options.SchemaName, ct).ConfigureAwait(false);

            foreach (var script in migrations)
            {
                if (await TwiceAsync(connection, script, script.Apply, script.NoTransaction, ApplyStep, ApplyTwiceStep, ct).ConfigureAwait(false) is { } failed)
                    return new MigrationIdempotencyReport(set, [], failed);
            }

            var applied = Schema(await DatabaseSnapshot.TakeAsync(connection, ct).ConfigureAwait(false));
            foreach (var script in Enumerable.Reverse(migrations))
            {
                if (await TwiceAsync(connection, script, script.Rollback, noTransaction: false, RollbackStep, RollbackTwiceStep, ct).ConfigureAwait(false) is { } failed)
                    return new MigrationIdempotencyReport(set, [], failed);
            }

            foreach (var script in migrations)
            {
                if (await TryRunAsync(connection, script.Apply, script.NoTransaction, ct).ConfigureAwait(false) is { } reapplyError)
                    return new MigrationIdempotencyReport(set, [], new MigrationIdempotencyFailure(script.Label, ReapplyStep, reapplyError));
            }

            var reapplied = Schema(await DatabaseSnapshot.TakeAsync(connection, ct).ConfigureAwait(false));
            if (reapplied != applied)
            {
                return new MigrationIdempotencyReport(set, [], new MigrationIdempotencyFailure(
                    migrations.Count == 0 ? "*" : migrations[^1].Label, ReapplyStep, $"the schema differs from the first apply: {Difference(applied, reapplied)}"));
            }
        }

        return new MigrationIdempotencyReport(set, migrations.Select(script => script.Label).ToList(), null);
    }

    /// <summary>Runs a script, snapshots, runs it again and snapshots: the second run must succeed and change nothing.</summary>
    private async Task<MigrationIdempotencyFailure?> TwiceAsync(
        DbConnection connection, Script script, string sql, bool noTransaction, string firstStep, string secondStep, CancellationToken ct)
    {
        if (await TryRunAsync(connection, sql, noTransaction, ct).ConfigureAwait(false) is { } firstError)
            return new MigrationIdempotencyFailure(script.Label, firstStep, firstError);

        var once = await DatabaseSnapshot.TakeAsync(connection, ct).ConfigureAwait(false);
        if (await TryRunAsync(connection, sql, noTransaction, ct).ConfigureAwait(false) is { } secondError)
            return new MigrationIdempotencyFailure(script.Label, secondStep, secondError);

        var twice = await DatabaseSnapshot.TakeAsync(connection, ct).ConfigureAwait(false);
        return once == twice ? null : new MigrationIdempotencyFailure(script.Label, secondStep, $"the second run changed the database: {Difference(once, twice)}");
    }

    /// <summary>Runs a script, returning the provider's error instead of throwing.</summary>
    private async Task<string?> TryRunAsync(DbConnection connection, string sql, bool noTransaction, CancellationToken ct)
    {
        try
        {
            if (noTransaction)
            {
                foreach (var statement in parser.Parse(sql))
                    await connection.ExecuteAsync(new CommandDefinition(statement, cancellationToken: ct)).ConfigureAwait(false);
                return null;
            }

            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            return null;
        }
        catch (DbException exception)
        {
            return exception.SqlState is { } state ? $"{exception.Message} ({state})" : exception.Message;
        }
    }

    /// <summary>Substitutes the set's own schema and every registered set's <c>{{schema:set}}</c>.</summary>
    private string Resolve(IMigrationDialect dialect, MigrationOptions options, string sql)
    {
        var resolved = MigrationConstants.SetSchemaPlaceholderPattern().Replace(sql, match =>
            registry.Contains(match.Groups[1].Value) ? registry.GetOptions(match.Groups[1].Value).SchemaName : match.Value);
        return resolved.Contains(MigrationConstants.SchemaPlaceholder, StringComparison.Ordinal)
            ? dialect.SubstituteSchema(resolved, options.SchemaName)
            : resolved;
    }

    /// <summary>Reads whether the script's leading comment header carries <c>-- @no-transaction</c>.</summary>
    private static bool IsNoTransaction(string applySql) => applySql
        .Split('\n')
        .Select(line => line.Trim())
        .TakeWhile(line => line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
        .Any(line => line.Replace(" ", "", StringComparison.Ordinal).Equals(MigrationConstants.NoTransactionDirectiveCompact, StringComparison.OrdinalIgnoreCase));

    /// <summary>Keeps a snapshot's structure — schemas, relations, columns — and drops its rows.</summary>
    private static string Schema(string snapshot) =>
        string.Join('\n', snapshot.Split('\n').Where(line => !line.StartsWith("rows ", StringComparison.Ordinal)));

    private static string Difference(string before, string after)
    {
        var old = before.Split('\n').ToHashSet(StringComparer.Ordinal);
        var now = after.Split('\n').ToHashSet(StringComparer.Ordinal);
        return string.Join("; ", old.Except(now).Select(line => "- " + line).Concat(now.Except(old).Select(line => "+ " + line)).Take(10));
    }

    /// <summary>Holds one migration's scripts, placeholders resolved.</summary>
    /// <param name="Label">The <c>NNN-name</c> label.</param>
    /// <param name="Apply">The Apply script.</param>
    /// <param name="Rollback">The Rollback script.</param>
    /// <param name="NoTransaction">Whether the Apply script runs outside a transaction.</param>
    private sealed record Script(string Label, string Apply, string Rollback, bool NoTransaction);
}
