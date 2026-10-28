namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents what one writing verb changed in one set: the migrations or seeds it touched and how long it waited for the set's lock.</summary>
public sealed record MigrationOperationReport
{
    /// <summary>Gets a report of a verb that found its target state and changed nothing.</summary>
    public static MigrationOperationReport Unchanged { get; } = new();

    /// <summary>Gets the migrations or seeds the verb applied, rolled back, forgot, repaired or recorded, in the order it touched them.</summary>
    public IReadOnlyList<MigrationLabel> Labels { get; init; } = [];

    /// <summary>Gets whether the verb changed anything — a claim or unlock changes state without touching a migration.</summary>
    public bool Changed { get; init; }

    /// <summary>Gets how long the verb waited for the set's advisory locks, in milliseconds.</summary>
    public long LockWaitMs { get; init; }

    /// <summary>Gets what the verb did beyond its labels, such as the marker a claim replaced or the sessions an unlock ended; null when nothing.</summary>
    public string? Detail { get; init; }
}
