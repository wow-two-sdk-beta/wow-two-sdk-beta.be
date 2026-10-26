using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines running pending migrations.</summary>
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
    /// <returns>The labels applied, or a <c>DataIntegrity</c> failure on drift or disallowed orphans, or a <c>Validation</c> failure on a malformed source.</returns>
    /// <exception cref="System.Data.Common.DbException">A migration script failed; its batch is rolled back and the provider's exception is rethrown.</exception>
    Task<Result<IReadOnlyList<string>>> ApplyPendingAsync(string appliedBy, CancellationToken ct = default);

    /// <summary>Rehearses every pending transactional migration under the advisory lock inside one transaction, then always rolls it back.</summary>
    /// <remarks>
    ///   - the rehearsal takes the same DDL locks as a real apply until it rolls back, so run it as a pre-deploy step, not on a timer
    ///   - <c>-- @no-transaction</c> scripts cannot run inside the transaction and are reported as not rehearsed
    ///   - non-transactional effects, such as sequence advances, are not rolled back
    /// </remarks>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The rehearsal report (a failed script is reported, not thrown), or the same drift, orphan, and malformed-source failures as apply.</returns>
    Task<Result<MigrationDryRunReport>> DryRunPendingAsync(CancellationToken ct = default);

    /// <summary>Computes the current state: applied, pending, drifted, and orphaned migrations.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Result<MigrationStatus>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Rolls back the most recent migration, or every migration above the given ordinal (dev/test only).</summary>
    /// <param name="targetOrdinal">The ordinal to roll back to, or null to roll back only the latest.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The labels rolled back, or a <c>NotFound</c> failure when an applied ordinal has no source migration.</returns>
    /// <exception cref="InvalidOperationException">Rollback is disabled — configuration, so it throws rather than returning a failure.</exception>
    Task<Result<IReadOnlyList<string>>> RollbackAsync(int? targetOrdinal = null, CancellationToken ct = default);

    /// <summary>Re-records the stored checksums of drifted migrations to match the source under the advisory lock (dev/test only).</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Repair is disabled (<see cref="MigrationOptions.AllowRollback"/> is false) — configuration, so it throws.</exception>
    Task<Result<IReadOnlyList<string>>> RepairAsync(CancellationToken ct = default);

    /// <summary>Records source migrations as applied without running them, so an existing database whose schema already matches adopts the set.</summary>
    /// <remarks>
    ///   - runs no script: each row is stamped <see cref="MigrationConstants.BaselineAppliedBy"/> with zero duration
    ///   - refuses a set that already records history, so it never hides a pending migration of a migrated set
    ///   - claims an owned schema under this host's owner and instance; the claim and the rows commit in one transaction
    ///   - not gated by <see cref="MigrationOptions.AllowRollback"/>: adopting a live database is a production step; the CLI confirms the target
    /// </remarks>
    /// <param name="throughOrdinal">The last source ordinal to mark as applied, or null for every source migration.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The labels marked as applied, or a <c>Validation</c> failure when the ordinal is not in the source, or a <c>Conflict</c> failure when the set records history or another owner holds the schema.</returns>
    Task<Result<IReadOnlyList<string>>> BaselineAsync(int? throughOrdinal = null, CancellationToken ct = default);

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
