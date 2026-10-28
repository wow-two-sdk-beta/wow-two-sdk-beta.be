using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.Parsers;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Provides the apply, status, level-check, plan, seed and operator verbs of one migration set behind every host.</summary>
/// <remarks>
///   - one host migrates a set at a time, under the set's advisory locks, waiting at most <see cref="MigrationOptions.AdvisoryLockWait"/>
///   - a schema claimed by another set or owner, checksum drift, an orphan below the level or an incompatible ahead fails the apply before any script runs
///   - apply flow, lock scope, transaction handling, kinds and migration sets: bespoke.md
/// </remarks>
public sealed partial class MigrationRunnerService(
    IMigrationBroker migrations,
    IMigrationHistoryRepository history,
    IDbConnectionFactory connections,
    MigrationOptions options,
    ILogger<MigrationRunnerService> logger,
    IMigrationChecksumHasher checksumHasher,
    ISqlStatementParser statementParser,
    TimeProvider clock,
    IMigrationDialect dialect,
    IOptions<MigrationAuditOptions>? audit = null) : IMigrationRunnerService
{
    /// <summary>The host stamp on the history rows a dry run inserts and rolls back.</summary>
    private const string DryRunAppliedBy = "dry-run";

    /// <summary>The host stamp a <c>claim</c> records on the owner marker.</summary>
    private const string ClaimAppliedBy = "claim";

    private readonly MigrationSchemaOwnerStore _owners = new(dialect, options);
    private readonly MigrationSetLock _lock = new(dialect, options);
    private readonly MigrationEventStore _events = new(dialect, options, clock, audit);
    private readonly MigrationSeeder _seeder = new(dialect, options, clock);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> ApplyPendingAsync(string appliedBy, CancellationToken ct = default) =>
        (await ApplyAsync(appliedBy, ct)).Map<IReadOnlyList<string>>(report => report.Labels.Select(label => label.Label).ToList());

    /// <inheritdoc />
    /// <remarks>
    ///   - <see cref="MigrationTransactionScope.Batch"/> commits consecutive transactional migrations together; a failure rolls back the batch
    ///   - a no-transaction script commits the batch before it, runs bare, then a new batch starts
    ///   - every script runs under <c>lock_timeout</c>; a batch whose table lock times out runs again, at most <see cref="MigrationOptions.LockAttempts"/> times
    ///   - a crash between a no-transaction script and its history row leaves it applied but unrecorded; the next run re-executes it, so it must be idempotent
    /// </remarks>
    public async Task<Result<MigrationOperationReport>> ApplyAsync(string appliedBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedBy);
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationOperationReport>.Fail(malformed);

        await using var conn = await connections.CreateOpenAsync(ct);
        if (await FindMissingExtensionsAsync(conn, ct) is { Count: > 0 } missing)
            return Result<MigrationOperationReport>.Fail(MissingExtensionsError(missing));

        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            var guard = await GuardSchemaAsync(conn, null, appliedBy, ct);
            if (guard.Conflict is { } claimed)
                return Result<MigrationOperationReport>.Fail(claimed);
            if (guard.Claimed)
                LogSchemaClaimed(options.SchemaName, options.SetName, options.SchemaOwner!, options.OwnerInstance!);

            await history.EnsureTableAsync(conn, null, ct);
            var applied = await ReadAppliedAsync(conn, null, source, backfill: true, ct);

            if (VerifyHistory(source, applied) is { } refused)
                return Result<MigrationOperationReport>.Fail(refused);

            var pending = Pending(source, applied);
            if (VerifyPending(pending, applied) is { } refusedPending)
                return Result<MigrationOperationReport>.Fail(refusedPending);

            if (pending.Count == 0)
            {
                LogUpToDate(applied.Count);
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });
            }

            if ((await MigrationSetSchemaResolver.ResolveAsync(dialect, _owners, conn, null, pending, ct)).IsFailure(out var unresolved, out var runnable))
                return Result<MigrationOperationReport>.Fail(unresolved);

            var committed = new List<MigrationDescriptor>();
            var inFlight = new List<MigrationDescriptor>();
            foreach (var unit in SplitIntoUnits(runnable))
            {
                bool done;
                try
                {
                    done = await RetryOnLockAsync(
                        () => unit is [{ NoTransaction: true } bare]
                            ? ApplyBareAsync(conn, bare, appliedBy, inFlight, ct)
                            : ApplyTransactionalAsync(conn, unit, appliedBy, inFlight, ct),
                        unit[0].Label, ct);
                }
                catch (Exception failure)
                {
                    LogUnitFailure(failure, unit, inFlight, committed.Select(m => m.Label).ToList(), runnable);
                    throw;
                }

                if (!done)
                {
                    return Result<MigrationOperationReport>.Fail(Busy(
                        $"a table lock {unit[0].Label} needs stayed taken through {options.LockAttempts} attempts; committed before it: {JoinOrNone(committed.Select(m => m.Label))}",
                        waitMs));
                }

                committed.AddRange(unit);
            }

            return Result<MigrationOperationReport>.Ok(new MigrationOperationReport
            {
                Labels = committed.Select(MigrationLabel.Of).ToList(),
                Changed = committed.Count > 0,
                LockWaitMs = waitMs,
            });
        }
        finally
        {
            await ReleaseLockAsync(conn);
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

    /// <summary>Applies a unit of transactional migrations and their history rows in one transaction, each script under its lock timeout.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="unit">The migrations committed together.</param>
    /// <param name="appliedBy">The host stamp recorded on each row.</param>
    /// <param name="inFlight">Receives each migration as it starts, so a failure can name what rolled back.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task ApplyTransactionalAsync(
        DbConnection conn, IReadOnlyList<MigrationDescriptor> unit, string appliedBy, List<MigrationDescriptor> inFlight, CancellationToken ct)
    {
        inFlight.Clear();
        var elapsed = new List<long>(unit.Count);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var migration in unit)
            {
                inFlight.Add(migration);
                await dialect.SetLockTimeoutAsync(conn, tx, migration.LockTimeout ?? options.LockTimeout, ct);
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

    /// <summary>Applies one no-transaction migration statement by statement under a session lock timeout, then records it in a separate statement.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="migration">The no-transaction migration, e.g. <c>CREATE INDEX CONCURRENTLY</c>.</param>
    /// <param name="appliedBy">The host stamp recorded on the row.</param>
    /// <param name="inFlight">Receives the migration as it starts, so a failure can name it.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task ApplyBareAsync(
        DbConnection conn, MigrationDescriptor migration, string appliedBy, List<MigrationDescriptor> inFlight, CancellationToken ct)
    {
        inFlight.Clear();
        inFlight.Add(migration);
        var stopwatch = Stopwatch.StartNew();

        await dialect.SetLockTimeoutAsync(conn, null, migration.LockTimeout ?? options.LockTimeout, ct);
        try
        {
            // Execute each statement separately so concurrent-index recovery remains outside a transaction.
            foreach (var statement in statementParser.Parse(migration.ApplySql))
                await conn.ExecuteAsync(new CommandDefinition(statement, cancellationToken: ct));
        }
        finally
        {
            await ResetLockTimeoutAsync(conn);
        }

        stopwatch.Stop();
        await history.RecordAsync(conn, null, BuildEntry(migration, appliedBy, stopwatch.ElapsedMilliseconds), ct);
        LogApplied(migration.Label, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Runs a unit of work, running it again after a lock timeout, at most <see cref="MigrationOptions.LockAttempts"/> times.</summary>
    /// <param name="unit">The work; it must roll its own transaction back before it throws.</param>
    /// <param name="label">The first migration or seed of the unit, for the log.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Whether the unit ran; false when every attempt met a held table lock.</returns>
    private async Task<bool> RetryOnLockAsync(Func<Task> unit, string label, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await unit();
                return true;
            }
            catch (DbException failure) when (dialect.IsLockNotAvailable(failure))
            {
                if (attempt >= options.LockAttempts)
                {
                    LogLockGivenUp(label, attempt);
                    return false;
                }

                LogLockRetry(label, attempt, options.LockAttempts);
                await Task.Delay(options.LockRetryDelay * attempt, ct);
            }
        }
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

    /// <summary>Releases the set's advisory locks without masking an earlier failure; a broken session ends them anyway.</summary>
    /// <param name="conn">The connection that holds the locks.</param>
    private async Task ReleaseLockAsync(DbConnection conn)
    {
        try
        {
            await _lock.ReleaseAsync(conn, CancellationToken.None);
        }
        catch (DbException releaseFailure)
        {
            LogRollbackFailed(releaseFailure);
        }
        catch (InvalidOperationException releaseFailure)
        {
            LogRollbackFailed(releaseFailure);
        }
    }

    /// <summary>Restores the session's lock timeout after a no-transaction script without masking an earlier failure.</summary>
    /// <param name="conn">The connection.</param>
    private async Task ResetLockTimeoutAsync(DbConnection conn)
    {
        try
        {
            await dialect.ResetLockTimeoutAsync(conn, CancellationToken.None);
        }
        catch (DbException resetFailure)
        {
            LogRollbackFailed(resetFailure);
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
        List<string> committed, IReadOnlyList<MigrationDescriptor> pending)
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
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationDryRunReport>.Fail(malformed);

        await using var conn = await connections.CreateOpenAsync(ct);
        if (await FindMissingExtensionsAsync(conn, ct) is { Count: > 0 } missing)
            return Result<MigrationDryRunReport>.Fail(MissingExtensionsError(missing));

        // The same advisory locks as apply, so a rehearsal never interleaves with a real run.
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationDryRunReport>.Fail(LockBusy(waitMs));

        try
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                return await RehearseAsync(conn, tx, source, ct);
            }
            finally
            {
                // Always roll back: a dry run never commits schema or history.
                await RollBackTransactionAsync(tx);
            }
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    /// <summary>Runs every pending transactional migration and its history row inside the rehearsal transaction.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="tx">The rehearsal transaction, rolled back by the caller.</param>
    /// <param name="source">The scanned source migrations.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<Result<MigrationDryRunReport>> RehearseAsync(
        DbConnection conn, DbTransaction tx, List<MigrationDescriptor> source, CancellationToken ct)
    {
        // A dry run's claim rolls back with its transaction, so it is never reported.
        if ((await GuardSchemaAsync(conn, tx, DryRunAppliedBy, ct)).Conflict is { } claimed)
            return Result<MigrationDryRunReport>.Fail(claimed);

        await history.EnsureTableAsync(conn, tx, ct);
        var applied = await ReadAppliedAsync(conn, tx, source, backfill: false, ct);

        if (VerifyHistory(source, applied) is { } refused)
            return Result<MigrationDryRunReport>.Fail(refused);

        var pending = Pending(source, applied);
        if (VerifyPending(pending, applied) is { } refusedPending)
            return Result<MigrationDryRunReport>.Fail(refusedPending);

        if ((await MigrationSetSchemaResolver.ResolveAsync(dialect, _owners, conn, tx, pending, ct)).IsFailure(out var unresolved, out var runnable))
            return Result<MigrationDryRunReport>.Fail(unresolved);

        var notRehearsed = runnable.Where(m => m.NoTransaction).Select(m => m.Label).ToList();
        var succeeded = new List<string>();
        MigrationDryRunFailure? failure = null;

        foreach (var migration in runnable.Where(m => !m.NoTransaction))
        {
            try
            {
                await dialect.SetLockTimeoutAsync(conn, tx, migration.LockTimeout ?? options.LockTimeout, ct);
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
                    FollowsNotRehearsed = runnable.Any(m => m.NoTransaction && m.Ordinal < migration.Ordinal),
                };
                LogDryRunFailed(failure.Migration, failure.Error, JoinOrNone(succeeded));
                break;
            }
        }

        if (failure is null)
            LogDryRunPassed(succeeded.Count, runnable.Count, JoinOrNone(notRehearsed));

        return Result<MigrationDryRunReport>.Ok(new MigrationDryRunReport
        {
            WouldApply = runnable.Select(m => m.Label).ToList(),
            Succeeded = succeeded,
            NotRehearsed = notRehearsed,
            Failure = failure,
        });
    }

    /// <inheritdoc />
    public async Task<Result<MigrationPlan>> PlanAsync(CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationPlan>.Fail(malformed);

        await using var conn = await connections.CreateOpenAsync(ct);
        var applied = await history.ExistsAsync(conn, null, ct) ? await history.GetAppliedAsync(conn, null, ct) : [];

        return Result<MigrationPlan>.Ok(new MigrationPlan
        {
            Set = options.SetName,
            AppliedLevel = applied.Count == 0 ? 0 : applied.Max(row => row.Ordinal),
            ExpectedLevel = source.Count == 0 ? 0 : source.Max(migration => migration.Ordinal),
            Pending = Pending(source, applied),
        });
    }

    /// <inheritdoc />
    public async Task<Result<MigrationStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationStatus>.Fail(malformed);

        var byOrdinal = source.ToDictionary(m => m.Ordinal);

        await using var conn = await connections.CreateOpenAsync(ct);
        if (options.SchemaOwner is not null)
            await dialect.EnsureSchemaAsync(conn, null, options.SchemaName, ct);
        await history.EnsureTableAsync(conn, null, ct);
        var applied = await ReadAppliedAsync(conn, null, source, backfill: false, ct);
        var schemaOwner = await _owners.ReadAsync(conn, null, ct);

        // Diff source against applied to classify drift, orphans, and pending.
        var drifted = applied
            .Where(a => byOrdinal.TryGetValue(a.Ordinal, out var m) && m.Checksum != a.Checksum.Trim())
            .Select(a => byOrdinal[a.Ordinal])
            .ToList();
        var orphaned = applied.Where(a => !byOrdinal.ContainsKey(a.Ordinal)).Select(a => a.Ordinal).ToList();

        return Result<MigrationStatus>.Ok(new MigrationStatus
        {
            Applied = applied, Pending = Pending(source, applied), Drifted = drifted, Orphaned = orphaned, SchemaOwner = schemaOwner,
        });
    }

    /// <inheritdoc />
    /// <remarks>Reads the history, the owner marker and the extensions only; a fresh database stays untouched.</remarks>
    public async Task<Result<MigrationLevelReport>> CheckLevelAsync(CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationLevelReport>.Fail(malformed);

        await using var conn = await connections.CreateOpenAsync(ct);
        var applied = await history.ExistsAsync(conn, null, ct)
            ? await ReadAppliedAsync(conn, null, source, backfill: false, ct)
            : [];
        var schemaOwner = await _owners.ReadAsync(conn, null, ct);
        var missing = await FindMissingExtensionsAsync(conn, ct);

        var byOrdinal = source.ToDictionary(m => m.Ordinal);
        var drifted = applied
            .Where(a => byOrdinal.TryGetValue(a.Ordinal, out var m) && m.Checksum != a.Checksum.Trim())
            .Select(a => byOrdinal[a.Ordinal].Label)
            .ToList();

        return Result<MigrationLevelReport>.Ok(MigrationLevelReport.Create(
            options.SetName, byOrdinal.Keys, applied, Pending(source, applied).Select(m => m.Label).ToList(), drifted,
            options.AllowOrphanedHistory, schemaOwner, schemaOwner is null ? null : _owners.DescribeConflict(schemaOwner),
            options.Release, missing));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> RollbackAsync(int? targetOrdinal = null, CancellationToken ct = default)
    {
        if (!options.AllowRollback)
            throw new InvalidOperationException(
                "Rollback is disabled (MigrationOptions.AllowRollback = false). Enable it to permit this guarded recovery op (allowed in any environment, prod included).");

        return (await RollBackAboveAsync(targetOrdinal, ct)).Map<IReadOnlyList<string>>(report => report.Labels.Select(label => label.Label).ToList());
    }

    /// <inheritdoc />
    public Task<Result<MigrationOperationReport>> RollbackToAsync(int targetOrdinal, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetOrdinal);
        return RollBackAboveAsync(targetOrdinal, ct);
    }

    /// <summary>Runs <c>Rollback.sql</c> for every applied migration above the target, or only the latest, and records one event.</summary>
    /// <param name="targetOrdinal">The level to return to, or null to roll back only the latest migration.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<Result<MigrationOperationReport>> RollBackAboveAsync(int? targetOrdinal, CancellationToken ct)
    {
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationOperationReport>.Fail(malformed);

        var byOrdinal = source.ToDictionary(m => m.Ordinal);

        await using var conn = await connections.CreateOpenAsync(ct);
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            if ((await GuardSchemaAsync(conn, null, claimedBy: null, ct)).Conflict is { } claimed)
                return Result<MigrationOperationReport>.Fail(claimed);

            if (!await history.ExistsAsync(conn, null, ct))
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });

            var applied = (await history.GetAppliedAsync(conn, null, ct)).OrderByDescending(a => a.Ordinal).ToList();

            // Roll back everything strictly above the target, or just the single latest when no target is given.
            var rows = targetOrdinal is { } target ? applied.Where(a => a.Ordinal > target).ToList() : applied.Take(1).ToList();
            if (rows.Count == 0)
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });

            // Refuse before any script, so a rollback never stops halfway on a missing source.
            if (rows.FirstOrDefault(row => !byOrdinal.ContainsKey(row.Ordinal)) is { } orphan)
            {
                return Result<MigrationOperationReport>.Fail(AppErrorFactory.NotFound(
                    $"No migration source for {orphan.Label}; cannot roll back. Use forget for rows this image lacks."));
            }

            if ((await MigrationSetSchemaResolver.ResolveAsync(dialect, _owners, conn, null, rows.Select(row => byOrdinal[row.Ordinal]).ToList(), ct))
                .IsFailure(out var unresolved, out var toRollback))
                return Result<MigrationOperationReport>.Fail(unresolved);

            var done = new List<MigrationDescriptor>();
            var detail = targetOrdinal is { } to ? $"to {to.ToString("D3", CultureInfo.InvariantCulture)}" : "latest";
            try
            {
                foreach (var migration in toRollback)
                {
                    var ran = await RetryOnLockAsync(() => RollBackOneAsync(conn, migration, ct), migration.Label, ct);
                    if (!ran)
                    {
                        await RecordEventQuietlyAsync(conn, MigrationEventVerbs.Rollback, done, detail + ", stopped busy");
                        return Result<MigrationOperationReport>.Fail(Busy(
                            $"a table lock {migration.Label}'s rollback needs stayed taken through {options.LockAttempts} attempts", waitMs));
                    }

                    done.Add(migration);
                    LogRolledBack(migration.Label);
                }
            }
            catch
            {
                await RecordEventQuietlyAsync(conn, MigrationEventVerbs.Rollback, done, detail + ", stopped by a failure");
                throw;
            }

            await _events.RecordAsync(conn, null, MigrationEventVerbs.Rollback, done.Select(m => m.Ordinal), detail, ct);
            return Result<MigrationOperationReport>.Ok(new MigrationOperationReport
            {
                Labels = done.Select(MigrationLabel.Of).ToList(),
                Changed = true,
                LockWaitMs = waitMs,
            });
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    /// <summary>Runs one migration's Rollback script and deletes its row in one transaction under its lock timeout.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="migration">The migration to roll back.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task RollBackOneAsync(DbConnection conn, MigrationDescriptor migration, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await dialect.SetLockTimeoutAsync(conn, tx, migration.LockTimeout ?? options.LockTimeout, ct);
            await conn.ExecuteAsync(new CommandDefinition(migration.RollbackSql, transaction: tx, cancellationToken: ct));
            await history.RemoveAsync(conn, tx, migration.Ordinal, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await RollBackTransactionAsync(tx);
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>A row's kind is the recorded one, else the kind of the source file the image carries; a row with neither counts as unsafe.</remarks>
    public async Task<Result<MigrationOperationReport>> ForgetAboveAsync(int aboveOrdinal, bool force = false, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aboveOrdinal);
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationOperationReport>.Fail(malformed);

        await using var conn = await connections.CreateOpenAsync(ct);
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            if ((await GuardSchemaAsync(conn, null, claimedBy: null, ct)).Conflict is { } claimed)
                return Result<MigrationOperationReport>.Fail(claimed);

            if (!await history.ExistsAsync(conn, null, ct))
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });

            var applied = await ReadAppliedAsync(conn, null, source, backfill: false, ct);
            var rows = applied.Where(row => row.Ordinal > aboveOrdinal).ToList();
            if (rows.Count == 0)
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });

            var unsafeRows = rows.Where(row => MigrationCompatibility.Parse(row.Kind) != MigrationKind.Expand).ToList();
            if (unsafeRows.Count > 0 && !force)
            {
                return Result<MigrationOperationReport>.Fail(AppError.Of(
                    AppErrorType.Conflict,
                    $"Set '{options.SetName}': forgetting {string.Join(", ", unsafeRows.Select(Describe))} would hide a contract or a row of unknown kind; " +
                    "confirm with force to forget them anyway.",
                    new Dictionary<string, object?> { ["unsafe"] = unsafeRows.Select(row => row.Ordinal).ToList() }));
            }

            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in rows)
                    await history.RemoveAsync(conn, tx, row.Ordinal, ct);
                await _events.RecordAsync(conn, tx, MigrationEventVerbs.Forget, rows.Select(row => row.Ordinal),
                    $"above {aboveOrdinal.ToString("D3", CultureInfo.InvariantCulture)}{(unsafeRows.Count > 0 ? ", forced" : "")}", ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await RollBackTransactionAsync(tx);
                throw;
            }

            LogForgot(options.SetName, JoinOrNone(rows.Select(row => row.Label)));
            return Result<MigrationOperationReport>.Ok(new MigrationOperationReport
            {
                Labels = rows.Select(MigrationLabel.Of).ToList(),
                Changed = true,
                LockWaitMs = waitMs,
            });
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }

        static string Describe(MigrationHistoryEntry row) => $"{row.Label} ({row.Kind ?? "no kind"})";
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> RepairAsync(CancellationToken ct = default)
    {
        if (!options.AllowRollback)
            throw new InvalidOperationException(
                "Repair is disabled (MigrationOptions.AllowRollback = false) — it rewrites recorded checksums; enable it to permit this guarded recovery op.");

        return (await RepairChecksumsAsync(ct)).Map<IReadOnlyList<string>>(report => report.Labels.Select(label => label.Label).ToList());
    }

    /// <inheritdoc />
    public async Task<Result<MigrationOperationReport>> RepairChecksumsAsync(CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationOperationReport>.Fail(malformed);

        var byOrdinal = source.ToDictionary(m => m.Ordinal);

        await using var conn = await connections.CreateOpenAsync(ct);

        // Repair mutates the history, so hold the same advisory locks the apply and rollback loops use.
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            if ((await GuardSchemaAsync(conn, null, claimedBy: null, ct)).Conflict is { } claimed)
                return Result<MigrationOperationReport>.Fail(claimed);

            if (!await history.ExistsAsync(conn, null, ct))
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });

            // Re-record only the rows whose source checksum has drifted.
            var drifted = (await history.GetAppliedAsync(conn, null, ct))
                .Where(row => byOrdinal.TryGetValue(row.Ordinal, out var migration) && migration.Checksum != row.Checksum.Trim())
                .Select(row => byOrdinal[row.Ordinal])
                .ToList();
            if (drifted.Count == 0)
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });

            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var migration in drifted)
                    await history.UpdateChecksumAsync(conn, tx, migration.Ordinal, migration.Checksum, ct);
                await _events.RecordAsync(conn, tx, MigrationEventVerbs.Repair, drifted.Select(m => m.Ordinal), detail: null, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await RollBackTransactionAsync(tx);
                throw;
            }

            return Result<MigrationOperationReport>.Ok(new MigrationOperationReport
            {
                Labels = drifted.Select(MigrationLabel.Of).ToList(),
                Changed = true,
                LockWaitMs = waitMs,
            });
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> BaselineAsync(int? throughOrdinal = null, CancellationToken ct = default) =>
        (await BaselineToAsync(throughOrdinal, ct)).Map<IReadOnlyList<string>>(report => report.Labels.Select(label => label.Label).ToList());

    /// <inheritdoc />
    public async Task<Result<MigrationOperationReport>> BaselineToAsync(int? throughOrdinal = null, CancellationToken ct = default)
    {
        if (Scan().IsFailure(out var malformed, out var source))
            return Result<MigrationOperationReport>.Fail(malformed);

        if (throughOrdinal is { } through && source.All(m => m.Ordinal != through))
        {
            return Result<MigrationOperationReport>.Fail(AppErrorFactory.Validation(
                $"Set '{options.SetName}' has no migration {through:D3} in its source; a baseline stops at a source migration."));
        }

        var adopted = source.Where(m => throughOrdinal is not { } last || m.Ordinal <= last).ToList();

        await using var conn = await connections.CreateOpenAsync(ct);
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            // The claim, the history table, every row and the event commit together, so a refused or failed baseline records nothing.
            await using var tx = await conn.BeginTransactionAsync(ct);
            var outcome = await RecordBaselineAsync(conn, tx, adopted, ct);
            if (outcome.Error is { } error)
            {
                await RollBackTransactionAsync(tx);
                return Result<MigrationOperationReport>.Fail(error);
            }

            if (!outcome.Recorded)
            {
                // A rerun through the same ordinal finds every row recorded: change nothing.
                await RollBackTransactionAsync(tx);
                return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });
            }

            await tx.CommitAsync(ct);
            if (outcome.Claimed)
                LogSchemaClaimed(options.SchemaName, options.SetName, options.SchemaOwner!, options.OwnerInstance!);
            LogBaselined(options.SetName, adopted.Count, JoinOrNone(adopted.Select(m => m.Label)));
            return Result<MigrationOperationReport>.Ok(new MigrationOperationReport
            {
                Labels = adopted.Select(MigrationLabel.Of).ToList(),
                Changed = true,
                LockWaitMs = waitMs,
            });
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    /// <summary>Claims an owned schema and records the adopted migrations and the event inside the baseline's transaction.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="tx">The baseline transaction.</param>
    /// <param name="adopted">The source migrations to mark as applied.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The refusal, or none with whether rows were recorded and the schema was claimed.</returns>
    private async Task<(AppError? Error, bool Recorded, bool Claimed)> RecordBaselineAsync(
        DbConnection conn, DbTransaction tx, List<MigrationDescriptor> adopted, CancellationToken ct)
    {
        try
        {
            var guard = await GuardSchemaAsync(conn, tx, MigrationConstants.BaselineAppliedBy, ct);
            if (guard.Conflict is { } claimed)
                return (claimed, false, false);

            await history.EnsureTableAsync(conn, tx, ct);
            var applied = await history.GetAppliedAsync(conn, tx, ct);
            if (applied.Count > 0)
            {
                var recorded = applied.ToDictionary(row => row.Ordinal);
                if (adopted.All(m => recorded.TryGetValue(m.Ordinal, out var row) && row.Checksum.Trim() == m.Checksum))
                    return (null, false, false);

                return (AppErrorFactory.Conflict(
                    $"Set '{options.SetName}' already records {applied.Count} applied migrations; a baseline adopts only a set with no history. " +
                    "Apply the pending migrations instead."), false, false);
            }

            foreach (var migration in adopted)
                await history.RecordAsync(conn, tx, BuildEntry(migration, MigrationConstants.BaselineAppliedBy, elapsedMs: 0), ct);
            await _events.RecordAsync(conn, tx, MigrationEventVerbs.Baseline, adopted.Select(m => m.Ordinal), detail: null, ct);

            return (null, true, guard.Claimed);
        }
        catch
        {
            await RollBackTransactionAsync(tx);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<MigrationOperationReport>> ClaimSchemaAsync(string owner, string instance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 120 || string.IsNullOrWhiteSpace(instance) || instance.Length > 200)
        {
            return Result<MigrationOperationReport>.Fail(AppErrorFactory.Validation(
                "A claim needs an owner of 1 to 120 characters and an instance of 1 to 200 characters."));
        }

        if (!MigrationConstants.SchemaNamePattern().IsMatch(options.SchemaName))
        {
            return Result<MigrationOperationReport>.Fail(AppErrorFactory.Validation(
                $"Set '{options.SetName}' cannot claim schema '{options.SchemaName}': not a lowercase identifier."));
        }

        await using var conn = await connections.CreateOpenAsync(ct);
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await dialect.EnsureSchemaAsync(conn, tx, options.SchemaName, ct);
                await _owners.EnsureTableAsync(conn, tx, ct);
                var marker = await _owners.ReadAsync(conn, tx, ct);
                if (marker is not null && marker.SetName == options.SetName && marker.Owner == owner && marker.Instance == instance)
                {
                    await RollBackTransactionAsync(tx);
                    return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged with { LockWaitMs = waitMs });
                }

                var detail = marker is null
                    ? $"claimed schema '{options.SchemaName}' for set '{options.SetName}' as '{owner}' of '{instance}'"
                    : $"moved schema '{options.SchemaName}' from set '{marker.SetName}' as '{marker.Owner}' of '{marker.Instance}' " +
                      $"to set '{options.SetName}' as '{owner}' of '{instance}'";
                await _owners.ReplaceAsync(conn, tx, owner, instance, clock.GetUtcNow(), ClaimAppliedBy, ct);
                await _events.RecordAsync(conn, tx, MigrationEventVerbs.Claim, [], detail, ct);
                await tx.CommitAsync(ct);

                LogClaimed(detail);
                return Result<MigrationOperationReport>.Ok(new MigrationOperationReport { Changed = true, LockWaitMs = waitMs, Detail = detail });
            }
            catch
            {
                await RollBackTransactionAsync(tx);
                throw;
            }
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MigrationLockHolder>>> GetLockHoldersAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.CreateOpenAsync(ct);
        return Result<IReadOnlyList<MigrationLockHolder>>.Ok(await dialect.GetLockHoldersAsync(conn, _lock.Keys, ct));
    }

    /// <inheritdoc />
    /// <remarks>Takes no lock — the holder it ends is the one that has it — and records the event bare.</remarks>
    public async Task<Result<MigrationOperationReport>> UnlockAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.CreateOpenAsync(ct);
        var holders = await dialect.GetLockHoldersAsync(conn, _lock.Keys, ct);
        if (holders.Count == 0)
            return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged);

        var ended = new List<string>();
        foreach (var holder in holders.DistinctBy(holder => holder.Pid))
        {
            if (await dialect.TerminateSessionAsync(conn, holder.Pid, ct))
                ended.Add($"pid {holder.Pid} ({holder.ApplicationName ?? "no application name"}, {holder.Key} key)");
        }

        if (ended.Count == 0)
            return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged);

        var detail = "ended " + string.Join("; ", ended);
        await _events.RecordAsync(conn, null, MigrationEventVerbs.Unlock, [], detail, ct);
        LogUnlocked(options.SetName, detail);
        return Result<MigrationOperationReport>.Ok(new MigrationOperationReport { Changed = true, Detail = detail });
    }

    /// <inheritdoc />
    public async Task<Result<MigrationOperationReport>> SeedAsync(string appliedBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedBy);
        if (ScanSeeds().IsFailure(out var malformed, out var seeds))
            return Result<MigrationOperationReport>.Fail(malformed);
        if (seeds.Count == 0)
            return Result<MigrationOperationReport>.Ok(MigrationOperationReport.Unchanged);

        await using var conn = await connections.CreateOpenAsync(ct);
        var (acquired, waitMs) = await _lock.AcquireAsync(conn, ct);
        if (!acquired)
            return Result<MigrationOperationReport>.Fail(LockBusy(waitMs));

        try
        {
            if ((await GuardSchemaAsync(conn, null, claimedBy: null, ct)).Conflict is { } claimed)
                return Result<MigrationOperationReport>.Fail(claimed);

            var journal = await _seeder.ReadJournalAsync(conn, ct);
            var changed = seeds.Where(seed => journal.TryGetValue(seed.Name, out var checksum) && checksum != seed.Checksum).ToList();
            if (changed.Count > 0)
            {
                return Result<MigrationOperationReport>.Fail(AppError.Of(
                    AppErrorType.Conflict,
                    $"Set '{options.SetName}': applied seed files changed since they ran: {string.Join(", ", changed.Select(seed => seed.Name))}. " +
                    "A seed file runs once; ship a correction as a new file.",
                    new Dictionary<string, object?> { ["changed"] = changed.Select(seed => seed.Name).ToList() }));
            }

            var applied = new List<MigrationSeedFile>();
            foreach (var seed in seeds.Where(seed => !journal.ContainsKey(seed.Name)))
            {
                var ran = await RetryOnLockAsync(() => ApplySeedAsync(conn, seed, appliedBy, ct), seed.Name, ct);
                if (!ran)
                {
                    return Result<MigrationOperationReport>.Fail(Busy(
                        $"a table lock seed {seed.Name} needs stayed taken through {options.LockAttempts} attempts; seeded before it: {JoinOrNone(applied.Select(s => s.Name))}",
                        waitMs));
                }

                applied.Add(seed);
                LogSeeded(seed.Name, seed.Tables.Sum(table => table.Rows.Count));
            }

            return Result<MigrationOperationReport>.Ok(new MigrationOperationReport
            {
                Labels = applied.Select(seed => new MigrationLabel(seed.Name, null)).ToList(),
                Changed = applied.Count > 0,
                LockWaitMs = waitMs,
            });
        }
        finally
        {
            await ReleaseLockAsync(conn);
        }
    }

    /// <summary>Applies one seed file and its journal row in one transaction under the set's lock timeout.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="seed">The seed file.</param>
    /// <param name="appliedBy">The host stamp recorded on the journal row.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task ApplySeedAsync(DbConnection conn, MigrationSeedFile seed, string appliedBy, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await dialect.SetLockTimeoutAsync(conn, tx, options.LockTimeout, ct);
            await _seeder.ApplyAsync(conn, tx, seed, appliedBy, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await RollBackTransactionAsync(tx);
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>Finds the set's schema through its owner marker, else the one schema holding <c>{set}_migration_history</c>.</remarks>
    public async Task<int?> ReadSetLevelAsync(string setName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setName);

        await using var conn = await connections.CreateOpenAsync(ct);
        var table = setName == MigrationConstants.DefaultSetName ? "migration_history" : $"{setName}_migration_history";
        var schema = await _owners.FindSchemaOfSetAsync(conn, null, setName, ct);
        if (schema is null && await dialect.FindTableSchemasAsync(conn, null, table, ct) is [var only])
            schema = only;

        if (schema is null || !await dialect.TableExistsAsync(conn, null, schema, table, ct))
            return null;

        var level = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            $"SELECT MAX(ordinal) FROM {dialect.QualifyHistoryTable(schema, table)}", cancellationToken: ct));
        return level ?? 0;
    }

    /// <summary>Creates an owned schema, refuses a schema another set, owner or instance has claimed, and records this set's claim when asked.</summary>
    /// <param name="conn">The open connection holding the advisory lock.</param>
    /// <param name="tx">The enclosing transaction, such as a dry run's, or null to run bare.</param>
    /// <param name="claimedBy">The host stamp to create the schema and record a first claim under, or null to only verify the marker.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The conflict error, or none when this set may migrate the schema, and whether this call recorded the claim.</returns>
    private async Task<(AppError? Conflict, bool Claimed)> GuardSchemaAsync(DbConnection conn, DbTransaction? tx, string? claimedBy, CancellationToken ct)
    {
        if (options.SchemaOwner is not null && claimedBy is not null)
            await dialect.EnsureSchemaAsync(conn, tx, options.SchemaName, ct);

        if (await _owners.ReadAsync(conn, tx, ct) is { } marker)
            return (_owners.DescribeConflict(marker) is { } conflict ? AppErrorFactory.Conflict(conflict) : null, false);

        if (options.SchemaOwner is null || claimedBy is null)
            return (null, false);

        await _owners.EnsureTableAsync(conn, tx, ct);
        await _owners.ClaimAsync(conn, tx, clock.GetUtcNow(), claimedBy, ct);
        return (null, true);
    }

    /// <summary>Reads the history with each kind-less row's kind filled from the source, recording it first when asked.</summary>
    /// <param name="conn">The open connection.</param>
    /// <param name="tx">The enclosing transaction, or null.</param>
    /// <param name="source">The scanned source migrations.</param>
    /// <param name="backfill">Whether to record the filled kinds — apply does, under the set's lock.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<IReadOnlyList<MigrationHistoryEntry>> ReadAppliedAsync(
        DbConnection conn, DbTransaction? tx, List<MigrationDescriptor> source, bool backfill, CancellationToken ct)
    {
        var applied = await history.GetAppliedAsync(conn, tx, ct);
        var bySource = source.ToDictionary(m => m.Ordinal);
        var filled = MigrationCompatibility.WithSourceKinds(applied, bySource);

        if (backfill)
        {
            var recorded = 0;
            for (var index = 0; index < applied.Count; index++)
            {
                if (applied[index].Kind is null && filled[index].Kind is { } kind)
                {
                    await history.UpdateKindAsync(conn, tx, applied[index].Ordinal, kind, filled[index].ContractSince, ct);
                    recorded++;
                }
            }

            if (recorded > 0)
                LogKindsRecorded(options.SetName, recorded);
        }

        return filled;
    }

    /// <summary>Applies the history rules before any script: no drift, no orphan below the level, and no ahead this release may not run on.</summary>
    /// <param name="source">The scanned source migrations.</param>
    /// <param name="applied">The applied rows, kinds filled.</param>
    /// <returns>The refusal, or null when the run may proceed.</returns>
    private AppError? VerifyHistory(List<MigrationDescriptor> source, IReadOnlyList<MigrationHistoryEntry> applied)
    {
        var byOrdinal = source.ToDictionary(m => m.Ordinal);
        var drifted = applied
            .Where(a => byOrdinal.TryGetValue(a.Ordinal, out var m) && m.Checksum != a.Checksum.Trim())
            .Select(a => byOrdinal[a.Ordinal].Label)
            .ToList();
        if (drifted.Count > 0)
        {
            return AppError.Of(
                AppErrorType.DataIntegrity,
                $"Applied migrations no longer match their source: {string.Join(", ", drifted)}.",
                new Dictionary<string, object?> { ["drifted"] = drifted });
        }

        var expectedLevel = source.Count == 0 ? 0 : source.Max(m => m.Ordinal);
        var orphaned = applied.Where(a => !byOrdinal.ContainsKey(a.Ordinal)).ToList();
        if (orphaned.Count == 0)
            return null;

        if (options.AllowOrphanedHistory)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                LogOrphanedHistory(string.Join(", ", orphaned.Select(o => o.Ordinal.ToString("D3", CultureInfo.InvariantCulture))));
            return null;
        }

        var below = orphaned.Where(row => row.Ordinal < expectedLevel).Select(row => row.Ordinal).ToList();
        if (below.Count > 0)
        {
            return AppError.Of(
                AppErrorType.DataIntegrity,
                $"History holds applied migrations absent from the source below its level: {string.Join(", ", below.Select(o => o.ToString("D3", CultureInfo.InvariantCulture)))}.",
                new Dictionary<string, object?> { ["orphaned"] = below });
        }

        var incompatible = MigrationCompatibility.IncompatibleAhead(applied, expectedLevel, options.Release);
        if (incompatible.Count > 0)
        {
            return AppError.Of(
                AppErrorType.DataIntegrity,
                $"Release {options.Release ?? "unknown"} may not run on newer history: {string.Join(", ", incompatible.Select(row => $"{row.Label} ({row.Kind ?? "no kind"}{(row.ContractSince is { } since ? $" since {since}" : "")})"))}.",
                new Dictionary<string, object?> { ["orphaned"] = incompatible.Select(row => row.Ordinal).ToList(), ["incompatible"] = true });
        }

        LogCompatibleAhead(options.SetName, JoinOrNone(orphaned.Select(row => row.Label)), options.Release ?? "unknown");
        return null;
    }

    /// <summary>Applies the pending rules before any script: no ordinal below the applied level outside a local run, and no unmet <c>@requires</c>.</summary>
    /// <param name="pending">The pending migrations.</param>
    /// <param name="applied">The applied rows.</param>
    /// <returns>The refusal, or null when the run may proceed.</returns>
    private AppError? VerifyPending(IReadOnlyList<MigrationDescriptor> pending, IReadOnlyList<MigrationHistoryEntry> applied)
    {
        var appliedLevel = applied.Count == 0 ? 0 : applied.Max(row => row.Ordinal);
        var below = pending.Where(m => m.Ordinal < appliedLevel).ToList();
        if (below.Count > 0 && options.LocalRun != true)
        {
            return AppError.Of(
                AppErrorType.Conflict,
                $"Set '{options.SetName}': pending migrations sit below the applied level {appliedLevel:D3}: {string.Join(", ", below.Select(m => m.Label))}. " +
                $"Renumber them above {appliedLevel:D3}; only a local run applies them in place.",
                new Dictionary<string, object?> { ["belowLevel"] = below.Select(m => m.Label).ToList() });
        }

        var requiring = pending.Where(m => m.Requires.Count > 0).ToList();
        if (requiring.Count > 0)
        {
            return AppError.Of(
                AppErrorType.Conflict,
                $"Set '{options.SetName}': {string.Join(", ", requiring.Select(m => $"{m.Label} requires {string.Join(", ", m.Requires)}"))}; " +
                "a required background migration must report finished first.",
                new Dictionary<string, object?> { ["requires"] = requiring.SelectMany(m => m.Requires).Distinct().ToList() });
        }

        return null;
    }

    /// <summary>Lists the source migrations the history lacks, in ordinal order.</summary>
    /// <param name="source">The scanned source migrations.</param>
    /// <param name="applied">The applied rows.</param>
    private static List<MigrationDescriptor> Pending(List<MigrationDescriptor> source, IReadOnlyList<MigrationHistoryEntry> applied)
    {
        var appliedOrdinals = applied.Select(a => a.Ordinal).ToHashSet();
        return source.Where(m => !appliedOrdinals.Contains(m.Ordinal)).ToList();
    }

    /// <summary>Lists the required extensions the database lacks; reads the catalog only when the set requires any.</summary>
    /// <param name="conn">The open connection.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<IReadOnlyList<string>> FindMissingExtensionsAsync(DbConnection conn, CancellationToken ct)
    {
        if (options.RequiredExtensions.Count == 0)
            return [];

        var installed = await dialect.GetInstalledExtensionsAsync(conn, ct);
        return options.RequiredExtensions.Where(extension => installed is null || !installed.Contains(extension)).Distinct(StringComparer.Ordinal).ToList();
    }

    private AppError MissingExtensionsError(IReadOnlyList<string> missing) => AppError.Of(
        AppErrorType.Conflict,
        $"Set '{options.SetName}' needs extensions the database lacks: {string.Join(", ", missing)}. Create them as a superuser before the migrate step.",
        new Dictionary<string, object?> { ["missingExtensions"] = missing });

    private AppError LockBusy(long waitMs) => Busy(
        $"another session held its migrator lock for {waitMs} ms, past the {options.AdvisoryLockWait.TotalSeconds:0.#} s bound; run locks to see the holder",
        waitMs);

    private AppError Busy(string reason, long waitMs) => AppError.Of(
        AppErrorType.DbTimeout,
        $"Set '{options.SetName}' is busy: {reason}.",
        new Dictionary<string, object?> { [MigrationConstants.BusyMetadataKey] = true, ["lockWaitMs"] = waitMs });

    /// <summary>Records an event while a failure is already in flight, logging rather than masking a second failure.</summary>
    private async Task RecordEventQuietlyAsync(DbConnection conn, string verb, List<MigrationDescriptor> done, string detail)
    {
        if (done.Count == 0)
            return;

        try
        {
            await _events.RecordAsync(conn, null, verb, done.Select(m => m.Ordinal), detail, CancellationToken.None);
        }
        catch (DbException eventFailure)
        {
            LogRollbackFailed(eventFailure);
        }
    }

    /// <summary>Reads the source, parses <c>NNN-name</c> and each header, computes checksums, and returns migrations ordered by ordinal.</summary>
    private Result<List<MigrationDescriptor>> Scan()
    {
        if (migrations.Read().IsFailure(out var sourceError, out var rawMigrations))
            return Result<List<MigrationDescriptor>>.Fail(sourceError);

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

            if (MigrationHeaderParser.Parse(raw.Name, raw.ApplySql).IsFailure(out var headerError, out var header))
                return Result<List<MigrationDescriptor>>.Fail(headerError);

            var usesSchema = raw.ApplySql.Contains(MigrationConstants.SchemaPlaceholder, StringComparison.Ordinal)
                || raw.RollbackSql.Contains(MigrationConstants.SchemaPlaceholder, StringComparison.Ordinal);
            if (usesSchema && !MigrationConstants.SchemaNamePattern().IsMatch(options.SchemaName))
            {
                return Result<List<MigrationDescriptor>>.Fail(AppErrorFactory.Validation(
                    $"Migration '{raw.Name}' uses {MigrationConstants.SchemaPlaceholder}, but SchemaName '{options.SchemaName}' " +
                    "is not a lowercase identifier (a-z, 0-9 and _, 63 characters at most)."));
            }

            descriptors.Add(new MigrationDescriptor
            {
                Ordinal = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                Name = match.Groups[2].Value,
                ApplySql = usesSchema ? dialect.SubstituteSchema(raw.ApplySql, options.SchemaName) : raw.ApplySql,
                RollbackSql = usesSchema ? dialect.SubstituteSchema(raw.RollbackSql, options.SchemaName) : raw.RollbackSql,
                // The checksum covers the script as authored, so one source reads the same under every schema.
                Checksum = checksumHasher.Hash(raw.ApplySql),
                NoTransaction = header.NoTransaction,
                Kind = header.Kind,
                ContractSince = header.ContractSince,
                Requires = header.Requires,
                LockTimeout = header.LockTimeout,
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

    /// <summary>Reads and validates the set's seed files, ordered by ordinal.</summary>
    private Result<List<MigrationSeedFile>> ScanSeeds()
    {
        if (migrations.ReadSeeds().IsFailure(out var sourceError, out var rawSeeds))
            return Result<List<MigrationSeedFile>>.Fail(sourceError);

        var seeds = new List<MigrationSeedFile>(rawSeeds.Count);
        foreach (var raw in rawSeeds)
        {
            if (MigrationSeedParser.Parse(raw, checksumHasher).IsFailure(out var error, out var seed))
                return Result<List<MigrationSeedFile>>.Fail(error);
            seeds.Add(seed);
        }

        var duplicate = seeds.GroupBy(seed => seed.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Result<List<MigrationSeedFile>>.Fail(AppErrorFactory.Validation(
                $"Duplicate seed ordinal {duplicate.Key:D3}: {string.Join(", ", duplicate.Select(seed => seed.Name))}."));
        }

        return Result<List<MigrationSeedFile>>.Ok(seeds.OrderBy(seed => seed.Ordinal).ToList());
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
        ExecutionMs = elapsedMs,
        Kind = MigrationCompatibility.ToText(migration.Kind),
        ContractSince = migration.ContractSince,
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

    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "Rolling back a migration transaction or releasing a lock failed; disposing the connection ends it.")]
    private partial void LogRollbackFailed(Exception exception);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Information, Message = "Dry run rehearsed {Rehearsed} of {Pending} pending migrations and rolled back. Not rehearsed (no-transaction): {NotRehearsed}.")]
    private partial void LogDryRunPassed(int rehearsed, int pending, string notRehearsed);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning, Message = "Dry run failed at {Migration}: {Error}. Succeeded before it: {Succeeded}. The rehearsal was rolled back.")]
    private partial void LogDryRunFailed(string migration, string error, string succeeded);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Information, Message = "Schema {Schema} claimed by set {Set} as {Owner} of instance {Instance}.")]
    private partial void LogSchemaClaimed(string schema, string set, string owner, string instance);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning, Message = "Baselined set {Set}: marked {Count} migrations as applied without running them: {Migrations}.")]
    private partial void LogBaselined(string set, int count, string migrations);

    [LoggerMessage(EventId = 3016, Level = LogLevel.Warning, Message = "Set {Set} is ahead of this release by migrations it survives ({Migrations}); release {Release} proceeds.")]
    private partial void LogCompatibleAhead(string set, string migrations, string release);

    [LoggerMessage(EventId = 3017, Level = LogLevel.Warning, Message = "A table lock {Migration} needs is taken (attempt {Attempt} of {Attempts}); the batch rolled back and runs again.")]
    private partial void LogLockRetry(string migration, int attempt, int attempts);

    [LoggerMessage(EventId = 3018, Level = LogLevel.Error, Message = "A table lock {Migration} needs stayed taken through {Attempts} attempts; the run reports busy.")]
    private partial void LogLockGivenUp(string migration, int attempts);

    [LoggerMessage(EventId = 3019, Level = LogLevel.Information, Message = "Set {Set}: recorded the kind of {Count} history rows applied before kinds existed.")]
    private partial void LogKindsRecorded(string set, int count);

    [LoggerMessage(EventId = 3020, Level = LogLevel.Warning, Message = "Set {Set}: forgot history rows {Migrations} without running SQL.")]
    private partial void LogForgot(string set, string migrations);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Warning, Message = "Claim: {Detail}.")]
    private partial void LogClaimed(string detail);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Warning, Message = "Set {Set}: unlock {Detail}.")]
    private partial void LogUnlocked(string set, string detail);

    [LoggerMessage(EventId = 3023, Level = LogLevel.Information, Message = "Seeded {Seed}: {Rows} rows.")]
    private partial void LogSeeded(string seed, int rows);
}
