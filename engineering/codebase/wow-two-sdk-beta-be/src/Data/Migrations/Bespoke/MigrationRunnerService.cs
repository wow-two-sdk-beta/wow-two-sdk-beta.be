using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.Parsers;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Provides the migration apply, status, rollback, and repair operations behind every host.</summary>
/// <remarks>
///   - one host migrates at a time
///   - checksum drift or orphaned history fails the apply before any script runs
///   - apply flow, lock scope, and transaction handling: bespoke.md
/// </remarks>
public sealed partial class MigrationRunnerService(
    IMigrationBroker migrations,
    IMigrationHistoryRepository history,
    IDbConnectionFactory connections,
    MigrationOptions options,
    ILogger<MigrationRunnerService> logger,
    IMigrationChecksumHasher checksumHasher,
    ISqlStatementParser statementParser,
    TimeProvider clock) : IMigrationRunnerService
{
    /// <summary>The host stamp on the history rows a dry run inserts and rolls back.</summary>
    private const string DryRunAppliedBy = "dry-run";

    /// <inheritdoc />
    /// <remarks>
    ///   - <see cref="MigrationTransactionScope.Batch"/> commits consecutive transactional migrations together; a failure rolls back the batch
    ///   - a no-transaction script commits the batch before it, runs bare, then a new batch starts
    ///   - a crash between a no-transaction script and its history row leaves it applied but unrecorded
    ///   - the next run re-executes that script, so its Apply SQL must be idempotent
    /// </remarks>
    public async Task<Result<IReadOnlyList<string>>> ApplyPendingAsync(string appliedBy, CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var migrations))
        {
            return Result<IReadOnlyList<string>>.Fail(malformed);
        }

        await using var conn = await connections.CreateOpenAsync(ct);

        // Acquire the advisory lock so only one host migrates at a time.
        await history.AcquireLockAsync(conn, ct);
        try
        {
            await history.EnsureTableAsync(conn, null, ct);
            var applied = await history.GetAppliedAsync(conn, null, ct);

            if (VerifyNoDrift(migrations, applied) is { } drift)
            {
                return Result<IReadOnlyList<string>>.Fail(drift);
            }

            if (VerifyNoOrphans(migrations, applied) is { } orphans)
            {
                return Result<IReadOnlyList<string>>.Fail(orphans);
            }

            var appliedOrdinals = applied.Select(a => a.Ordinal).ToHashSet();
            var pending = migrations.Where(m => !appliedOrdinals.Contains(m.Ordinal)).ToList();
            if (pending.Count == 0)
            {
                LogUpToDate(applied.Count);
                return Result<IReadOnlyList<string>>.Ok([]);
            }

            var committed = new List<string>();
            var inFlight = new List<MigrationDescriptor>();
            foreach (var unit in SplitIntoUnits(pending))
            {
                inFlight.Clear();
                try
                {
                    if (unit is [{ NoTransaction: true } bare])
                        await ApplyBareAsync(conn, bare, appliedBy, inFlight, ct);
                    else
                        await ApplyTransactionalAsync(conn, unit, appliedBy, inFlight, ct);
                }
                catch (Exception failure)
                {
                    LogUnitFailure(failure, unit, inFlight, committed, pending);
                    throw;
                }

                committed.AddRange(unit.Select(m => m.Label));
            }

            return Result<IReadOnlyList<string>>.Ok(committed);
        }
        finally
        {
            await history.ReleaseLockAsync(conn, ct);
        }
    }

    /// <summary>Splits pending migrations into commit units: transactional batches and single no-transaction scripts.</summary>
    /// <param name="pending">The pending migrations, ordered by ordinal.</param>
    /// <remarks>Under <see cref="MigrationTransactionScope.PerMigration"/> every transactional migration is its own unit.</remarks>
    private List<IReadOnlyList<MigrationDescriptor>> SplitIntoUnits(IReadOnlyList<MigrationDescriptor> pending)
    {
        var units = new List<IReadOnlyList<MigrationDescriptor>>();
        var batch = new List<MigrationDescriptor>();
        foreach (var migration in pending)
        {
            if (migration.NoTransaction)
            {
                // A bare script cannot join a transaction, so it closes the batch before it.
                if (batch.Count > 0)
                    units.Add(batch);
                batch = [];
                units.Add([migration]);
                continue;
            }

            batch.Add(migration);
            if (options.TransactionScope == MigrationTransactionScope.PerMigration)
            {
                units.Add(batch);
                batch = [];
            }
        }

        if (batch.Count > 0)
            units.Add(batch);
        return units;
    }

    /// <summary>Applies a unit of transactional migrations and their history rows in one transaction.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="unit">The migrations committed together.</param>
    /// <param name="appliedBy">The host stamp recorded on each row.</param>
    /// <param name="inFlight">Receives each migration as it starts, so a failure can name what rolled back.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task ApplyTransactionalAsync(
        DbConnection conn, IReadOnlyList<MigrationDescriptor> unit, string appliedBy, List<MigrationDescriptor> inFlight, CancellationToken ct)
    {
        var elapsed = new List<long>(unit.Count);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var migration in unit)
            {
                inFlight.Add(migration);
                var stopwatch = Stopwatch.StartNew();
                await conn.ExecuteAsync(new CommandDefinition(migration.ApplySql, transaction: tx, cancellationToken: ct));
                stopwatch.Stop();
                await history.RecordAsync(conn, tx, BuildEntry(migration, appliedBy, stopwatch.ElapsedMilliseconds), ct);
                elapsed.Add(stopwatch.ElapsedMilliseconds);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await RollBackTransactionAsync(tx);
            throw;
        }

        // Report each migration only after its batch commits.
        for (var i = 0; i < unit.Count; i++)
            LogApplied(unit[i].Label, elapsed[i]);
    }

    /// <summary>Applies one no-transaction migration statement by statement, then records it in a separate statement.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="migration">The no-transaction migration, e.g. <c>CREATE INDEX CONCURRENTLY</c>.</param>
    /// <param name="appliedBy">The host stamp recorded on the row.</param>
    /// <param name="inFlight">Receives the migration as it starts, so a failure can name it.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task ApplyBareAsync(
        DbConnection conn, MigrationDescriptor migration, string appliedBy, List<MigrationDescriptor> inFlight, CancellationToken ct)
    {
        inFlight.Add(migration);
        var stopwatch = Stopwatch.StartNew();

        // Execute each statement separately so concurrent-index recovery remains outside a transaction.
        foreach (var statement in statementParser.Parse(migration.ApplySql))
            await conn.ExecuteAsync(new CommandDefinition(statement, cancellationToken: ct));
        stopwatch.Stop();
        await history.RecordAsync(conn, null, BuildEntry(migration, appliedBy, stopwatch.ElapsedMilliseconds), ct);
        LogApplied(migration.Label, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Rolls back a failed or rehearsal transaction without masking an earlier failure.</summary>
    /// <param name="tx">The transaction to roll back.</param>
    private async Task RollBackTransactionAsync(DbTransaction tx)
    {
        try
        {
            await tx.RollbackAsync(CancellationToken.None);
        }
        catch (DbException rollbackFailure)
        {
            LogRollbackFailed(rollbackFailure);
        }
        catch (InvalidOperationException rollbackFailure)
        {
            LogRollbackFailed(rollbackFailure);
        }
    }

    /// <summary>Logs which migrations a failed unit rolled back, which stay committed, and which never ran.</summary>
    /// <param name="failure">The exception that stopped the unit.</param>
    /// <param name="unit">The unit that failed.</param>
    /// <param name="inFlight">The unit's migrations that started, the failing one last.</param>
    /// <param name="committed">The labels committed earlier in this run.</param>
    /// <param name="pending">Every pending migration of this run, ordered by ordinal.</param>
    private void LogUnitFailure(
        Exception failure, IReadOnlyList<MigrationDescriptor> unit, List<MigrationDescriptor> inFlight,
        List<string> committed, List<MigrationDescriptor> pending)
    {
        var failed = inFlight.Count > 0 ? inFlight[^1] : unit[0];
        var committedLabels = JoinOrNone(committed);
        var notAttempted = JoinOrNone(pending.Where(m => m.Ordinal > failed.Ordinal).Select(m => m.Label));

        if (failed.NoTransaction)
            LogBareFailure(failure, failed.Label, committedLabels, notAttempted);
        else
            LogBatchRolledBack(failure, failed.Label, JoinOrNone(inFlight.Select(m => m.Label)), committedLabels, notAttempted);
    }

    /// <summary>Joins labels for a log line, or returns <c>none</c> when there are none.</summary>
    /// <param name="labels">The labels to join.</param>
    private static string JoinOrNone(IEnumerable<string> labels)
    {
        var joined = string.Join(", ", labels);
        return joined.Length == 0 ? "none" : joined;
    }

    /// <inheritdoc />
    /// <remarks>
    ///   - the history-table create and read share the rehearsal transaction, so a fresh database stays untouched
    ///   - the rehearsal stops at the first failure: the transaction is aborted from then on
    /// </remarks>
    public async Task<Result<MigrationDryRunReport>> DryRunPendingAsync(CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var migrations))
        {
            return Result<MigrationDryRunReport>.Fail(malformed);
        }

        await using var conn = await connections.CreateOpenAsync(ct);

        // The same advisory lock as apply, so a rehearsal never interleaves with a real run.
        await history.AcquireLockAsync(conn, ct);
        try
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                return await RehearseAsync(conn, tx, migrations, ct);
            }
            finally
            {
                // Always roll back: a dry run never commits schema or history.
                await RollBackTransactionAsync(tx);
            }
        }
        finally
        {
            await history.ReleaseLockAsync(conn, ct);
        }
    }

    /// <summary>Runs every pending transactional migration and its history row inside the rehearsal transaction.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="tx">The rehearsal transaction, rolled back by the caller.</param>
    /// <param name="migrations">The scanned source migrations.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<Result<MigrationDryRunReport>> RehearseAsync(
        DbConnection conn, DbTransaction tx, List<MigrationDescriptor> migrations, CancellationToken ct)
    {
        await history.EnsureTableAsync(conn, tx, ct);
        var applied = await history.GetAppliedAsync(conn, tx, ct);

        if (VerifyNoDrift(migrations, applied) is { } drift)
        {
            return Result<MigrationDryRunReport>.Fail(drift);
        }

        if (VerifyNoOrphans(migrations, applied) is { } orphans)
        {
            return Result<MigrationDryRunReport>.Fail(orphans);
        }

        var appliedOrdinals = applied.Select(a => a.Ordinal).ToHashSet();
        var pending = migrations.Where(m => !appliedOrdinals.Contains(m.Ordinal)).ToList();
        var notRehearsed = pending.Where(m => m.NoTransaction).Select(m => m.Label).ToList();
        var succeeded = new List<string>();
        MigrationDryRunFailure? failure = null;

        foreach (var migration in pending.Where(m => !m.NoTransaction))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await conn.ExecuteAsync(new CommandDefinition(migration.ApplySql, transaction: tx, cancellationToken: ct));
                stopwatch.Stop();
                await history.RecordAsync(conn, tx, BuildEntry(migration, DryRunAppliedBy, stopwatch.ElapsedMilliseconds), ct);
                succeeded.Add(migration.Label);
            }
            catch (DbException error)
            {
                failure = new MigrationDryRunFailure
                {
                    Migration = migration.Label,
                    Error = error.Message,
                    SqlState = error.SqlState,
                    FollowsNotRehearsed = pending.Any(m => m.NoTransaction && m.Ordinal < migration.Ordinal),
                };
                LogDryRunFailed(failure.Migration, failure.Error, JoinOrNone(succeeded));
                break;
            }
        }

        if (failure is null)
            LogDryRunPassed(succeeded.Count, pending.Count, JoinOrNone(notRehearsed));

        return Result<MigrationDryRunReport>.Ok(new MigrationDryRunReport
        {
            WouldApply = pending.Select(m => m.Label).ToList(),
            Succeeded = succeeded,
            NotRehearsed = notRehearsed,
            Failure = failure,
        });
    }

    /// <inheritdoc />
    public async Task<Result<MigrationStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var migrations))
        {
            return Result<MigrationStatus>.Fail(malformed);
        }

        var byOrdinal = migrations.ToDictionary(m => m.Ordinal);

        await using var conn = await connections.CreateOpenAsync(ct);
        await history.EnsureTableAsync(conn, null, ct);
        var applied = await history.GetAppliedAsync(conn, null, ct);
        var appliedOrdinals = applied.Select(a => a.Ordinal).ToHashSet();

        // Diff source against applied to classify drift, orphans, and pending.
        var drifted = applied
            .Where(a => byOrdinal.TryGetValue(a.Ordinal, out var m) && m.Checksum != a.Checksum.Trim())
            .Select(a => byOrdinal[a.Ordinal])
            .ToList();
        var orphaned = applied.Where(a => !byOrdinal.ContainsKey(a.Ordinal)).Select(a => a.Ordinal).ToList();
        var pending = migrations.Where(m => !appliedOrdinals.Contains(m.Ordinal)).ToList();

        return Result<MigrationStatus>.Ok(
            new MigrationStatus { Applied = applied, Pending = pending, Drifted = drifted, Orphaned = orphaned });
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> RollbackAsync(int? targetOrdinal = null, CancellationToken ct = default)
    {
        if (!options.AllowRollback)
            throw new InvalidOperationException(
                "Rollback is disabled (MigrationOptions.AllowRollback = false). Enable it to permit this guarded recovery op (allowed in any environment, prod included).");

        if (Scan().IsFailure(out var malformed, out var migrations))
        {
            return Result<IReadOnlyList<string>>.Fail(malformed);
        }

        var byOrdinal = migrations.ToDictionary(m => m.Ordinal);

        await using var conn = await connections.CreateOpenAsync(ct);
        await history.AcquireLockAsync(conn, ct);
        try
        {
            await history.EnsureTableAsync(conn, null, ct);
            var applied = (await history.GetAppliedAsync(conn, null, ct)).OrderByDescending(a => a.Ordinal).ToList();

            // Roll back everything strictly above the target, or just the single latest when no target is given.
            var toRollback = targetOrdinal is { } target
                ? applied.Where(a => a.Ordinal > target).ToList()
                : applied.Take(1).ToList();

            var done = new List<string>();
            foreach (var row in toRollback)
            {
                if (!byOrdinal.TryGetValue(row.Ordinal, out var migration))
                {
                    return Result<IReadOnlyList<string>>.Fail(AppErrorFactory.NotFound(
                        $"No migration source for {row.Ordinal:D3}-{row.Name}; cannot roll back."));
                }

                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    await conn.ExecuteAsync(new CommandDefinition(migration.RollbackSql, transaction: tx, cancellationToken: ct));
                    await history.RemoveAsync(conn, tx, row.Ordinal, ct);
                    await tx.CommitAsync(ct);
                    done.Add(migration.Label);
                    LogRolledBack(migration.Label);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return Result<IReadOnlyList<string>>.Ok(done);
        }
        finally
        {
            await history.ReleaseLockAsync(conn, ct);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> RepairAsync(CancellationToken ct = default)
    {
        if (!options.AllowRollback)
            throw new InvalidOperationException(
                "Repair is disabled (MigrationOptions.AllowRollback = false) — it rewrites recorded checksums; enable it to permit this guarded recovery op.");

        if (Scan().IsFailure(out var malformed, out var migrations))
        {
            return Result<IReadOnlyList<string>>.Fail(malformed);
        }

        var byOrdinal = migrations.ToDictionary(m => m.Ordinal);

        await using var conn = await connections.CreateOpenAsync(ct);

        // Repair mutates migration_history, so hold the same advisory lock the apply/rollback loops use.
        await history.AcquireLockAsync(conn, ct);
        try
        {
            await history.EnsureTableAsync(conn, null, ct);
            var applied = await history.GetAppliedAsync(conn, null, ct);

            var repaired = new List<string>();
            foreach (var row in applied)
            {
                // Re-record only the rows whose source checksum has drifted.
                if (byOrdinal.TryGetValue(row.Ordinal, out var migration) && migration.Checksum != row.Checksum.Trim())
                {
                    await history.UpdateChecksumAsync(conn, row.Ordinal, migration.Checksum, ct);
                    repaired.Add(migration.Label);
                }
            }

            return Result<IReadOnlyList<string>>.Ok(repaired);
        }
        finally
        {
            await history.ReleaseLockAsync(conn, ct);
        }
    }

    /// <summary>Reads the source, parses <c>NNN-name</c>, computes checksums, and returns migrations ordered by ordinal.</summary>
    private Result<List<MigrationDescriptor>> Scan()
    {
        if (migrations.Read().IsFailure(out var sourceError, out var rawMigrations))
        {
            return Result<List<MigrationDescriptor>>.Fail(sourceError);
        }

        var descriptors = new List<MigrationDescriptor>();

        foreach (var raw in rawMigrations)
        {
            // Parse the NNN-name prefix; reject anything that does not match.
            var match = MigrationConstants.FolderPattern().Match(raw.Name);
            if (!match.Success)
            {
                return Result<List<MigrationDescriptor>>.Fail(AppErrorFactory.Validation(
                    $"Migration folder '{raw.Name}' must match NNN-name (e.g. 001-baseline)."));
            }

            descriptors.Add(new MigrationDescriptor
            {
                Ordinal = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                Name = match.Groups[2].Value,
                ApplySql = raw.ApplySql,
                RollbackSql = raw.RollbackSql,
                Checksum = checksumHasher.Hash(raw.ApplySql),
                NoTransaction = HasNoTransactionDirective(raw.ApplySql),
            });
        }

        // Reject two migrations claiming the same ordinal.
        var duplicate = descriptors.GroupBy(d => d.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return Result<List<MigrationDescriptor>>.Fail(AppErrorFactory.Validation(
                $"Duplicate migration ordinal {duplicate.Key:D3}: {string.Join(", ", duplicate.Select(d => d.Name))}."));
        }

        return Result<List<MigrationDescriptor>>.Ok(descriptors.OrderBy(d => d.Ordinal).ToList());
    }

    /// <summary>Gets whether the leading comment header contains a no-transaction directive.</summary>
    /// <param name="applySql">The Apply script to inspect.</param>
    private static bool HasNoTransactionDirective(string applySql)
    {
        using var reader = new StringReader(applySql);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (!trimmed.StartsWith("--", StringComparison.Ordinal))
                break; // The first non-comment line ends the header.
            if (trimmed.Replace(" ", "").Equals(MigrationConstants.NoTransactionDirectiveCompact, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Verifies no applied migration's source checksum has drifted.</summary>
    /// <param name="migrations">The scanned source migrations.</param>
    /// <param name="applied">The applied history rows to compare against.</param>
    /// <returns>The drift error, or <see langword="null"/> when every applied checksum still matches.</returns>
    private static AppError? VerifyNoDrift(IReadOnlyList<MigrationDescriptor> migrations, IReadOnlyList<MigrationHistoryEntry> applied)
    {
        var byOrdinal = migrations.ToDictionary(m => m.Ordinal);
        var drifted = applied
            .Where(a => byOrdinal.TryGetValue(a.Ordinal, out var m) && m.Checksum != a.Checksum.Trim())
            .Select(a => byOrdinal[a.Ordinal].Label)
            .ToList();

        if (drifted.Count == 0)
        {
            return null;
        }

        return AppError.Of(
            AppErrorType.DataIntegrity,
            $"Applied migrations no longer match their source: {string.Join(", ", drifted)}.",
            new Dictionary<string, object?> { ["drifted"] = drifted });
    }

    /// <summary>Fails closed when the history holds applied migrations absent from the source — unless orphans are explicitly allowed.</summary>
    /// <param name="migrations">The scanned source migrations.</param>
    /// <param name="applied">The applied history rows to compare against.</param>
    /// <returns>The orphan error, or <see langword="null"/> when there are none or they are allowed.</returns>
    private AppError? VerifyNoOrphans(IReadOnlyList<MigrationDescriptor> migrations, IReadOnlyList<MigrationHistoryEntry> applied)
    {
        var sourceOrdinals = migrations.Select(m => m.Ordinal).ToHashSet();
        var orphaned = applied.Where(a => !sourceOrdinals.Contains(a.Ordinal)).Select(a => a.Ordinal).ToList();
        if (orphaned.Count == 0)
        {
            return null;
        }

        if (options.AllowOrphanedHistory)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                LogOrphanedHistory(string.Join(", ", orphaned.Select(o => o.ToString("D3", CultureInfo.InvariantCulture))));
            return null;
        }

        return AppError.Of(
            AppErrorType.DataIntegrity,
            $"History holds applied migrations absent from the source: {string.Join(", ", orphaned.Select(o => o.ToString("D3", CultureInfo.InvariantCulture)))}.",
            new Dictionary<string, object?> { ["orphaned"] = orphaned });
    }

    /// <summary>Builds the history entry recorded for an applied migration.</summary>
    /// <param name="migration">The applied migration.</param>
    /// <param name="appliedBy">The host stamp recorded on the row.</param>
    /// <param name="elapsedMs">The apply duration in milliseconds.</param>
    private MigrationHistoryEntry BuildEntry(MigrationDescriptor migration, string appliedBy, long elapsedMs) => new()
    {
        Ordinal = migration.Ordinal,
        Version = options.Version,
        Name = migration.Name,
        Checksum = migration.Checksum,
        AppliedAt = clock.GetUtcNow(),
        AppliedBy = appliedBy,
        ExecutionMs = (int)elapsedMs,
    };

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Migrations up to date ({Count} applied).")]
    private partial void LogUpToDate(int count);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Applied {Migration} in {Elapsed}ms.")]
    private partial void LogApplied(string migration, long elapsed);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Rolled back {Migration}.")]
    private partial void LogRolledBack(string migration);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Applying over orphaned history (ordinals {Orphaned}) — running binary may be older than the database.")]
    private partial void LogOrphanedHistory(string orphaned);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error, Message = "Migration {Failed} failed; rolled back {RolledBack}, so none of them is recorded. Committed earlier in this run: {Committed}. Not attempted: {NotAttempted}.")]
    private partial void LogBatchRolledBack(Exception exception, string failed, string rolledBack, string committed, string notAttempted);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "No-transaction migration {Failed} failed outside a transaction; it is not recorded and may be partially applied. Committed earlier in this run: {Committed}. Not attempted: {NotAttempted}.")]
    private partial void LogBareFailure(Exception exception, string failed, string committed, string notAttempted);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "Rolling back a migration transaction failed; disposing the connection ends it.")]
    private partial void LogRollbackFailed(Exception exception);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Information, Message = "Dry run rehearsed {Rehearsed} of {Pending} pending migrations and rolled back. Not rehearsed (no-transaction): {NotRehearsed}.")]
    private partial void LogDryRunPassed(int rehearsed, int pending, string notRehearsed);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning, Message = "Dry run failed at {Migration}: {Error}. Succeeded before it: {Succeeded}. The rehearsal was rolled back.")]
    private partial void LogDryRunFailed(string migration, string error, string succeeded);
}
