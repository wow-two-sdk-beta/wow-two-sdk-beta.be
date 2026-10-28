using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines the operations on one migration set: apply, check, plan and the operator verbs.</summary>
/// <remarks>
///   - a failure carrying <see cref="MigrationConstants.BusyMetadataKey"/> means a lock was not acquired within its bound (exit <c>3</c>)
///   - any other failure is a refusal (exit <c>1</c>); a thrown <see cref="System.Data.Common.DbException"/> is a failed script or connection (exit <c>2</c>)
///   - the operator verbs (<c>RollbackToAsync</c>, <c>ForgetAboveAsync</c>, <c>RepairChecksumsAsync</c>, <c>BaselineToAsync</c>, <c>ClaimSchemaAsync</c>,
///     <c>UnlockAsync</c>) carry no <see cref="MigrationOptions.AllowRollback"/> gate: the caller confirms the target, and each records an event row
/// </remarks>
public interface IMigrationRunnerService
{
    /// <summary>Applies all pending migrations under the advisory lock, returning the labels applied (empty when up to date).</summary>
    /// <remarks>
    ///   - by default consecutive transactional migrations commit as one batch, so a failure rolls back the whole batch
    ///   - a <c>-- @no-transaction</c> script commits the batch before it and runs bare
    ///   - <see cref="MigrationOptions.TransactionScope"/> selects per-migration transactions instead
    /// </remarks>
    /// <param name="appliedBy">The host stamp recorded on each applied row, such as <c>startup</c>, <c>endpoint</c>, or <c>cli</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The labels applied, or a <c>DataIntegrity</c> failure on drift or disallowed orphans, a <c>Conflict</c> failure on a refused rule, a <c>Validation</c> failure on a malformed source, or a busy <c>DbTimeout</c> failure.</returns>
    /// <exception cref="System.Data.Common.DbException">A migration script failed; its batch is rolled back and the provider's exception is rethrown.</exception>
    Task<Result<IReadOnlyList<string>>> ApplyPendingAsync(string appliedBy, CancellationToken ct = default);

    /// <summary>Applies all pending migrations like <see cref="ApplyPendingAsync"/>, reporting each with its kind and the lock wait.</summary>
    /// <param name="appliedBy">The host stamp recorded on each applied row.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>What the run applied, or the same failures as <see cref="ApplyPendingAsync"/>.</returns>
    /// <exception cref="System.Data.Common.DbException">A migration script failed; its batch is rolled back and the provider's exception is rethrown.</exception>
    Task<Result<MigrationOperationReport>> ApplyAsync(string appliedBy, CancellationToken ct = default);

    /// <summary>Rehearses every pending transactional migration under the advisory lock inside one transaction, then always rolls it back.</summary>
    /// <remarks>
    ///   - the rehearsal takes the same DDL locks as a real apply until it rolls back, so run it as a pre-deploy step, not on a timer
    ///   - <c>-- @no-transaction</c> scripts cannot run inside the transaction and are reported as not rehearsed
    ///   - non-transactional effects, such as sequence advances, are not rolled back
    /// </remarks>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The rehearsal report (a failed script is reported, not thrown), or the same refusals as apply.</returns>
    Task<Result<MigrationDryRunReport>> DryRunPendingAsync(CancellationToken ct = default);

    /// <summary>Lists what apply would run, reading the history only when it exists — no lock, no write.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The plan, or a <c>Validation</c> failure on a malformed source.</returns>
    Task<Result<MigrationPlan>> PlanAsync(CancellationToken ct = default);

    /// <summary>Computes the current state: applied, pending, drifted, and orphaned migrations.</summary>
    /// <remarks>Creates the history table when missing; <see cref="CheckLevelAsync"/> is the read-only check.</remarks>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Result<MigrationStatus>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Rolls back the most recent migration, or every migration above the given ordinal (gated by <see cref="MigrationOptions.AllowRollback"/>).</summary>
    /// <param name="targetOrdinal">The ordinal to roll back to, or null to roll back only the latest.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The labels rolled back, or a <c>NotFound</c> failure when an applied ordinal has no source migration.</returns>
    /// <exception cref="InvalidOperationException">Rollback is disabled — configuration, so it throws rather than returning a failure.</exception>
    Task<Result<IReadOnlyList<string>>> RollbackAsync(int? targetOrdinal = null, CancellationToken ct = default);

    /// <summary>Runs <c>Rollback.sql</c> for every applied migration above <paramref name="targetOrdinal"/>, newest first, and records one event — the confirmed <c>rollback --to N</c> verb.</summary>
    /// <param name="targetOrdinal">The level to return to; at that level the call changes nothing.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The migrations rolled back, or a <c>NotFound</c> failure before any script when an applied ordinal has no source migration.</returns>
    Task<Result<MigrationOperationReport>> RollbackToAsync(int targetOrdinal, CancellationToken ct = default);

    /// <summary>Deletes every history row above <paramref name="aboveOrdinal"/> without running SQL, and records one event — the confirmed <c>forget --above N</c> verb.</summary>
    /// <remarks>For an older image refused by newer history: the rows go, the schema stays.</remarks>
    /// <param name="aboveOrdinal">The level to keep; rows above it are forgotten.</param>
    /// <param name="force">Whether to forget a contract or a row with no kind too.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The rows forgotten, or a <c>Conflict</c> failure naming the contract or kind-less rows when <paramref name="force"/> is off.</returns>
    Task<Result<MigrationOperationReport>> ForgetAboveAsync(int aboveOrdinal, bool force = false, CancellationToken ct = default);

    /// <summary>Re-records the stored checksums of drifted migrations to match the source under the advisory lock (gated by <see cref="MigrationOptions.AllowRollback"/>).</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Repair is disabled (<see cref="MigrationOptions.AllowRollback"/> is false) — configuration, so it throws.</exception>
    Task<Result<IReadOnlyList<string>>> RepairAsync(CancellationToken ct = default);

    /// <summary>Re-records drifted checksums and records one event — the confirmed <c>repair</c> verb.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The migrations repaired, empty when nothing drifted.</returns>
    Task<Result<MigrationOperationReport>> RepairChecksumsAsync(CancellationToken ct = default);

    /// <summary>Records source migrations as applied without running them, so an existing database whose schema already matches adopts the set.</summary>
    /// <remarks>
    ///   - runs no script: each row is stamped <see cref="MigrationConstants.BaselineAppliedBy"/> with zero duration
    ///   - a rerun through the same ordinal finds every row recorded and changes nothing
    ///   - refuses a set whose history lacks a migration it would adopt, so it never hides a pending migration of a migrated set
    ///   - claims an owned schema under this host's owner and instance; the claim, the rows and the event commit in one transaction
    /// </remarks>
    /// <param name="throughOrdinal">The last source ordinal to mark as applied, or null for every source migration.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The labels marked as applied, or a <c>Validation</c> failure when the ordinal is not in the source, or a <c>Conflict</c> failure when the set records other history or another owner holds the schema.</returns>
    Task<Result<IReadOnlyList<string>>> BaselineAsync(int? throughOrdinal = null, CancellationToken ct = default);

    /// <summary>Records source migrations as applied like <see cref="BaselineAsync"/>, reporting each with its kind — the confirmed <c>baseline --to N</c> verb.</summary>
    /// <param name="throughOrdinal">The last source ordinal to mark as applied, or null for every source migration.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Result<MigrationOperationReport>> BaselineToAsync(int? throughOrdinal = null, CancellationToken ct = default);

    /// <summary>Records the set's schema marker under <paramref name="owner"/> and <paramref name="instance"/>, replacing any marker, and records one event — the confirmed <c>claim</c> verb.</summary>
    /// <param name="owner">The owner kind, such as <c>module</c> or <c>service</c>.</param>
    /// <param name="instance">The instance, such as <c>identity-haven@prod</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The claim's report; unchanged when the marker already names this set, owner and instance.</returns>
    Task<Result<MigrationOperationReport>> ClaimSchemaAsync(string owner, string instance, CancellationToken ct = default);

    /// <summary>Lists the other sessions that hold the set's migrator locks, read-only.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Result<IReadOnlyList<MigrationLockHolder>>> GetLockHoldersAsync(CancellationToken ct = default);

    /// <summary>Ends every other session that holds the set's migrator locks and records one event — the confirmed <c>unlock</c> verb.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The report naming the sessions ended; unchanged when no session held the locks.</returns>
    Task<Result<MigrationOperationReport>> UnlockAsync(CancellationToken ct = default);

    /// <summary>Applies the set's seed files not yet journalled, one transaction per file, under the advisory lock.</summary>
    /// <param name="appliedBy">The host stamp recorded on each journal row.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The seeds applied, or a <c>Validation</c> failure on a malformed file, or a <c>Conflict</c> failure before any seed when a journalled file changed.</returns>
    /// <exception cref="System.Data.Common.DbException">A seed's statement failed; its transaction is rolled back.</exception>
    Task<Result<MigrationOperationReport>> SeedAsync(string appliedBy, CancellationToken ct = default);

    /// <summary>Reads another set's applied level from this set's database, finding its schema through the owner markers.</summary>
    /// <param name="setName">The other set, such as <c>identity</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Its highest applied ordinal, or null when no history of that set exists here.</returns>
    Task<int?> ReadSetLevelAsync(string setName, CancellationToken ct = default);

    /// <summary>Names the database the set connects to, read from its connection settings without connecting — what a guarded verb's <c>--confirm</c> must match.</summary>
    /// <returns>The database name (SQLite: the file name), or null when the runner cannot tell.</returns>
    string? ReadDatabaseName() => null;

    /// <summary>Compares the database's applied level with the source without writing anything — the production startup check.</summary>
    /// <remarks>
    ///   - never creates the schema, the history table or an owner marker, and takes no lock
    ///   - the default body derives the report from <see cref="GetStatusAsync"/>, which may create the history table
    /// </remarks>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The level report, or a <c>Validation</c> failure on a malformed source.</returns>
    async Task<Result<MigrationLevelReport>> CheckLevelAsync(CancellationToken ct = default)
    {
        if ((await GetStatusAsync(ct).ConfigureAwait(false)).IsFailure(out var error, out var status))
            return Result<MigrationLevelReport>.Fail(error);

        var orphaned = status.Orphaned.ToHashSet();
        var sourceOrdinals = status.Pending.Select(migration => migration.Ordinal)
            .Concat(status.Applied.Select(row => row.Ordinal).Where(ordinal => !orphaned.Contains(ordinal)))
            .ToList();

        return Result<MigrationLevelReport>.Ok(MigrationLevelReport.Create(
            MigrationConstants.DefaultSetName, sourceOrdinals, status.Applied,
            status.Pending.Select(migration => migration.Label).ToList(),
            status.Drifted.Select(migration => migration.Label).ToList(),
            allowOrphanedHistory: false, status.SchemaOwner, ownerConflict: null));
    }
}
