using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Writes a set's seed files and their journal, <c>{set}_seed_history</c>: one transaction per file, every table it fills.</summary>
/// <param name="dialect">The set's dialect.</param>
/// <param name="options">The set's options: schema and journal name.</param>
/// <param name="clock">The clock stamping <c>applied_at</c>.</param>
internal sealed class MigrationSeeder(IMigrationDialect dialect, MigrationOptions options, TimeProvider clock)
{
    private string Journal => dialect.QualifyHistoryTable(options.SchemaName, options.SeedHistoryTableName);

    /// <summary>Reads each journalled seed's checksum by name, or nothing when the journal is absent; never creates it.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<IReadOnlyDictionary<string, string>> ReadJournalAsync(DbConnection connection, CancellationToken ct)
    {
        if (!await dialect.TableExistsAsync(connection, null, options.SchemaName, options.SeedHistoryTableName, ct).ConfigureAwait(false))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var rows = await connection.QueryAsync<(string Name, string Checksum)>(new CommandDefinition(
            $"SELECT name, checksum FROM {Journal}", cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Name, row => row.Checksum.Trim(), StringComparer.Ordinal);
    }

    /// <summary>Fills every table of one seed file and journals it, inside the caller's transaction.</summary>
    /// <param name="connection">The open connection holding the set's lock.</param>
    /// <param name="transaction">The file's transaction.</param>
    /// <param name="seed">The seed file.</param>
    /// <param name="appliedBy">The host stamp recorded on the journal row.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task ApplyAsync(DbConnection connection, DbTransaction transaction, MigrationSeedFile seed, string appliedBy, CancellationToken ct)
    {
        await dialect.EnsureSeedHistoryTableAsync(connection, transaction, options.SchemaName, options.SeedHistoryTableName, ct).ConfigureAwait(false);

        foreach (var table in seed.Tables)
        {
            var schema = table.Schema ?? options.SchemaName;
            var types = await dialect.GetColumnTypesAsync(connection, transaction, schema, table.Table, ct).ConfigureAwait(false);
            foreach (var row in table.Rows)
            {
                var (sql, parameters) = BuildUpsert(schema, table, row, types, seed.Mode);
                await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {Journal} (name, checksum, mode, applied_at, applied_by) VALUES (@name, @checksum, @mode, @appliedAt, @appliedBy)",
            new
            {
                name = seed.Name,
                checksum = seed.Checksum,
                mode = seed.Mode == MigrationSeedMode.Upsert ? "upsert" : "insert",
                appliedAt = clock.GetUtcNow(),
                appliedBy,
            },
            transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Builds one row's insert: every value a parameter, cast to its column's type where the dialect needs it.</summary>
    private (string Sql, DynamicParameters Parameters) BuildUpsert(
        string schema, MigrationSeedTable table, IReadOnlyList<KeyValuePair<string, JsonElement>> row,
        IReadOnlyDictionary<string, string> types, MigrationSeedMode mode)
    {
        var parameters = new DynamicParameters();
        var columns = new List<string>(row.Count);
        var values = new List<string>(row.Count);
        for (var index = 0; index < row.Count; index++)
        {
            var (column, value) = row[index];
            var name = "p" + index.ToString(CultureInfo.InvariantCulture);
            parameters.Add(name, ToValue(value));
            columns.Add(dialect.QuoteIdentifier(column));
            values.Add(dialect.CastParameter("@" + name, types.TryGetValue(column, out var type) ? type : null));
        }

        var updates = row.Select(pair => pair.Key).Where(column => !table.Key.Contains(column, StringComparer.Ordinal))
            .Select(column => $"{dialect.QuoteIdentifier(column)} = excluded.{dialect.QuoteIdentifier(column)}")
            .ToList();

        var sql = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"INSERT INTO {dialect.QualifyHistoryTable(schema, table.Table)} ({string.Join(", ", columns)}) ")
            .Append(CultureInfo.InvariantCulture, $"VALUES ({string.Join(", ", values)}) ")
            .Append(CultureInfo.InvariantCulture, $"ON CONFLICT ({string.Join(", ", table.Key.Select(dialect.QuoteIdentifier))}) ")
            .Append(mode == MigrationSeedMode.Upsert && updates.Count > 0 ? $"DO UPDATE SET {string.Join(", ", updates)}" : "DO NOTHING")
            .ToString();
        return (sql, parameters);
    }

    /// <summary>Converts a JSON value to the parameter it binds as.</summary>
    private static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var whole) => whole,
        JsonValueKind.Number when value.TryGetDecimal(out var exact) => exact,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };
}
