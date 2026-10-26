using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a read-only comparison of one set's applied level with the level the running code expects.</summary>
public sealed record MigrationLevelReport
{
    /// <summary>Gets the set the report describes.</summary>
    public required string Set { get; init; }

    /// <summary>Gets the highest ordinal in the code's source, or 0 when the source is empty.</summary>
    public required int ExpectedLevel { get; init; }

    /// <summary>Gets the highest ordinal the database records, or 0 when nothing is applied.</summary>
    public required int AppliedLevel { get; init; }

    /// <summary>Gets the labels of the source migrations the database has not applied.</summary>
    public required IReadOnlyList<string> Pending { get; init; }

    /// <summary>Gets the ordinals the database records with no migration in the source.</summary>
    public required IReadOnlyList<int> Orphaned { get; init; }

    /// <summary>Gets the labels of the applied migrations whose source changed since they ran.</summary>
    public required IReadOnlyList<string> Drifted { get; init; }

    /// <summary>Gets the schema's recorded owner marker, or null when the schema is unclaimed.</summary>
    public MigrationSchemaOwner? SchemaOwner { get; init; }

    /// <summary>Gets why the owner marker forbids this host the schema, or null when it does not.</summary>
    public string? OwnerConflict { get; init; }

    /// <summary>Gets the most severe difference between the database and the code.</summary>
    public required MigrationLevelState State { get; init; }

    /// <summary>Gets whether the database is exactly at the code's level.</summary>
    public bool IsCurrent => State == MigrationLevelState.Current;

    /// <summary>Describes the report in one line, for logs and the CLI.</summary>
    public string Describe() => State switch
    {
        MigrationLevelState.Current => $"{Set}: at {Level(AppliedLevel)}, current.",
        MigrationLevelState.Behind => $"{Set}: database at {Level(AppliedLevel)}, code expects {Level(ExpectedLevel)}; pending {string.Join(", ", Pending)}.",
        MigrationLevelState.Ahead => $"{Set}: database at {Level(AppliedLevel)} is newer than the code ({Level(ExpectedLevel)}).",
        MigrationLevelState.Orphaned => $"{Set}: history holds migrations absent from the source: {string.Join(", ", Orphaned.Select(Level))}.",
        MigrationLevelState.Drifted => $"{Set}: applied migrations changed since they ran: {string.Join(", ", Drifted)}.",
        _ => $"{Set}: {OwnerConflict}",
    };

    /// <summary>Classifies a set's source against its history, the most severe difference winning.</summary>
    /// <param name="set">The set the report describes.</param>
    /// <param name="sourceOrdinals">Every ordinal in the code's source.</param>
    /// <param name="applied">Every applied history row.</param>
    /// <param name="pending">The labels of the pending source migrations.</param>
    /// <param name="drifted">The labels of the drifted applied migrations.</param>
    /// <param name="allowOrphanedHistory">Whether orphans below the code's level are tolerated.</param>
    /// <param name="schemaOwner">The schema's recorded owner marker, if any.</param>
    /// <param name="ownerConflict">Why the marker forbids this host the schema, if it does.</param>
    internal static MigrationLevelReport Create(
        string set,
        IReadOnlyCollection<int> sourceOrdinals,
        IReadOnlyList<MigrationHistoryEntry> applied,
        IReadOnlyList<string> pending,
        IReadOnlyList<string> drifted,
        bool allowOrphanedHistory,
        MigrationSchemaOwner? schemaOwner,
        string? ownerConflict)
    {
        var source = sourceOrdinals.ToHashSet();
        var expectedLevel = source.Count == 0 ? 0 : source.Max();
        var appliedLevel = applied.Count == 0 ? 0 : applied.Max(row => row.Ordinal);
        var orphaned = applied.Select(row => row.Ordinal).Where(ordinal => !source.Contains(ordinal)).ToList();

        var state = ownerConflict is not null ? MigrationLevelState.OwnerConflict
            : appliedLevel > expectedLevel ? MigrationLevelState.Ahead
            : drifted.Count > 0 ? MigrationLevelState.Drifted
            : orphaned.Count > 0 && !allowOrphanedHistory ? MigrationLevelState.Orphaned
            : pending.Count > 0 ? MigrationLevelState.Behind
            : MigrationLevelState.Current;

        return new MigrationLevelReport
        {
            Set = set,
            ExpectedLevel = expectedLevel,
            AppliedLevel = appliedLevel,
            Pending = pending,
            Orphaned = orphaned,
            Drifted = drifted,
            SchemaOwner = schemaOwner,
            OwnerConflict = ownerConflict,
            State = state,
        };
    }

    private static string Level(int ordinal) => ordinal.ToString("D3", CultureInfo.InvariantCulture);
}
