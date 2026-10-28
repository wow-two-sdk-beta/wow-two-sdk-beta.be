namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines the migrate verbs over a host's registered sets, each returning a <see cref="MigrateRunResult"/> a command prints and exits with.</summary>
/// <remarks>
///   - every verb is idempotent: a rerun that finds its target state reports <see cref="MigrateOutcome.Current"/> and changes nothing
///   - <c>check</c>, <c>plan</c> and <c>locks</c> write nothing and take no lock; on an empty database <c>check</c> reports every set <see cref="MigrationLevelState.Behind"/>
///   - the writing operator verbs take one set — null names the host's only set — and carry no confirmation of their own: the caller confirms the database
///   - none creates the database; a migrate command creates it first through the set's dialect
/// </remarks>
public interface IMigrationEngine
{
    /// <summary>Checks each set's level read-only: <see cref="MigrateOutcome.Current"/> when the release may run (current or a compatible ahead), else refused.</summary>
    /// <param name="sets">The sets to check, or null for every set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> CheckAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default);

    /// <summary>Lists each set's pending migrations with their kinds, without a lock and without writing.</summary>
    /// <param name="sets">The sets to plan, or null for every set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> PlanAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default);

    /// <summary>Applies every set in dependency order, or rehearses it, after refusing before any SQL on an unmet dependency or a missing extension.</summary>
    /// <param name="sets">The sets to apply, or null for every set.</param>
    /// <param name="dryRun">Whether to rehearse inside a transaction that always rolls back.</param>
    /// <param name="appliedBy">The host stamp recorded on each applied row.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> ApplyAsync(IReadOnlyCollection<string>? sets = null, bool dryRun = false, string appliedBy = "deploy", CancellationToken ct = default);

    /// <summary>Applies every set's seed files not yet journalled, in dependency order — run after <see cref="ApplyAsync"/>.</summary>
    /// <param name="sets">The sets to seed, or null for every set.</param>
    /// <param name="appliedBy">The host stamp recorded on each journal row.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> SeedAsync(IReadOnlyCollection<string>? sets = null, string appliedBy = "deploy", CancellationToken ct = default);

    /// <summary>Rolls one set back to <paramref name="toOrdinal"/> by its <c>Rollback.sql</c> files; at that level it changes nothing.</summary>
    /// <param name="set">The set, or null for the host's only set.</param>
    /// <param name="toOrdinal">The level to return to.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> RollbackAsync(string? set, int toOrdinal, CancellationToken ct = default);

    /// <summary>Deletes one set's history rows above <paramref name="aboveOrdinal"/> without running SQL; refuses a contract or kind-less row unless forced.</summary>
    /// <param name="set">The set, or null for the host's only set.</param>
    /// <param name="aboveOrdinal">The level to keep.</param>
    /// <param name="force">Whether to forget a contract or kind-less row too.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> ForgetAsync(string? set, int aboveOrdinal, bool force = false, CancellationToken ct = default);

    /// <summary>Re-records one set's drifted checksums.</summary>
    /// <param name="set">The set, or null for the host's only set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> RepairAsync(string? set, CancellationToken ct = default);

    /// <summary>Records one set's migrations through <paramref name="throughOrdinal"/> as applied without running them; a rerun through the same ordinal changes nothing.</summary>
    /// <param name="set">The set, or null for the host's only set.</param>
    /// <param name="throughOrdinal">The last ordinal to record, or null for every source migration.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> BaselineAsync(string? set, int? throughOrdinal = null, CancellationToken ct = default);

    /// <summary>Records one set's schema marker under an owner and instance, replacing any marker.</summary>
    /// <param name="set">The set, or null for the host's only set.</param>
    /// <param name="owner">The owner kind, such as <c>module</c> or <c>service</c>.</param>
    /// <param name="instance">The instance, such as <c>identity-haven@prod</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> ClaimAsync(string? set, string owner, string instance, CancellationToken ct = default);

    /// <summary>Lists the sessions holding each set's migrator locks, read-only.</summary>
    /// <param name="sets">The sets to inspect, or null for every set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> LocksAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default);

    /// <summary>Ends every session holding one set's migrator locks.</summary>
    /// <param name="set">The set, or null for the host's only set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateRunResult> UnlockAsync(string? set, CancellationToken ct = default);
}
