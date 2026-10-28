namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents how one verb ended for one set — the entry of the <c>sets</c> array in the <c>migrate-result</c> line.</summary>
/// <remarks>Carries no connection string or other secret; a message names sets, migrations and rules only.</remarks>
public sealed record MigrateSetResult
{
    /// <summary>Gets the set name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the set's schema.</summary>
    public required string Schema { get; init; }

    /// <summary>Gets how the verb ended for this set.</summary>
    public required MigrateOutcome Status { get; init; }

    /// <summary>Gets the set's level state after the verb, or null when it could not be read.</summary>
    public MigrationLevelState? State { get; init; }

    /// <summary>Gets the highest applied ordinal before the verb, or null when it could not be read.</summary>
    public int? LevelBefore { get; init; }

    /// <summary>Gets the highest applied ordinal after the verb, or null when it could not be read.</summary>
    public int? LevelAfter { get; init; }

    /// <summary>Gets the migrations the verb recorded as applied: by <c>apply</c> or <c>baseline</c>.</summary>
    public IReadOnlyList<MigrationLabel> Applied { get; init; } = [];

    /// <summary>Gets the history rows the verb removed: by <c>rollback</c> or <c>forget</c>.</summary>
    public IReadOnlyList<MigrationLabel> Removed { get; init; } = [];

    /// <summary>Gets the migrations whose checksum <c>repair</c> re-recorded.</summary>
    public IReadOnlyList<MigrationLabel> Repaired { get; init; } = [];

    /// <summary>Gets the source migrations the database has not applied: from <c>check</c>, <c>plan</c> and a dry run.</summary>
    public IReadOnlyList<MigrationLabel> Pending { get; init; } = [];

    /// <summary>Gets the ordinals the history records with no source migration.</summary>
    public IReadOnlyList<int> Orphaned { get; init; } = [];

    /// <summary>Gets the applied migrations whose source changed since they ran.</summary>
    public IReadOnlyList<string> Drifted { get; init; } = [];

    /// <summary>Gets the rows above the code's level this release may not run on.</summary>
    public IReadOnlyList<string> IncompatibleAhead { get; init; } = [];

    /// <summary>Gets the required extensions the database lacks.</summary>
    public IReadOnlyList<string> MissingExtensions { get; init; } = [];

    /// <summary>Gets the dependencies the database does not meet, one line each.</summary>
    public IReadOnlyList<string> MissingDependencies { get; init; } = [];

    /// <summary>Gets the seed files <c>seed</c> applied.</summary>
    public IReadOnlyList<string> Seeds { get; init; } = [];

    /// <summary>Gets the sessions holding the set's migrator locks: from <c>locks</c>.</summary>
    public IReadOnlyList<MigrationLockHolder> Locks { get; init; } = [];

    /// <summary>Gets the set's background migrations still running; empty until background migrations ship.</summary>
    public IReadOnlyList<string> Background { get; init; } = [];

    /// <summary>Gets the owner release the image carries, or null when unknown.</summary>
    public string? Release { get; init; }

    /// <summary>Gets the rollback floor after the verb: the highest <c>since</c> among applied contracts.</summary>
    public string? RollbackFloor { get; init; }

    /// <summary>Gets how long the verb waited for the set's advisory locks, in milliseconds.</summary>
    public long LockWaitMs { get; init; }

    /// <summary>Gets why the set was refused, failed or is busy, or what the verb did beyond its lists; null when nothing needs saying.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the provider's SQLSTATE of a failed script, when it reports one.</summary>
    public string? SqlState { get; init; }
}
