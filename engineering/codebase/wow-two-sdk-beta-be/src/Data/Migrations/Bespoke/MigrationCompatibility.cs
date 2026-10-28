namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Decides whether a release may run on a set's history, and the set's rollback floor.</summary>
/// <remarks>
///   - release R may run when every applied ordinal above R's level is an expand, or a contract with <c>since ≤ R</c>
///   - a row without a kind, a contract with an unreadable <c>since</c>, or any contract when R is unknown counts as incompatible
/// </remarks>
internal static class MigrationCompatibility
{
    /// <summary>Holds the stored text of <see cref="MigrationKind.Expand"/>.</summary>
    public const string ExpandText = "expand";

    /// <summary>Holds the stored text of <see cref="MigrationKind.Contract"/>.</summary>
    public const string ContractText = "contract";

    /// <summary>Returns the stored text of a kind.</summary>
    /// <param name="kind">The kind.</param>
    public static string ToText(MigrationKind kind) => kind == MigrationKind.Contract ? ContractText : ExpandText;

    /// <summary>Reads a stored kind, or null for a row recorded before kinds or with an unknown value.</summary>
    /// <param name="text">The stored text.</param>
    public static MigrationKind? Parse(string? text) => text?.Trim() switch
    {
        ExpandText => MigrationKind.Expand,
        ContractText => MigrationKind.Contract,
        _ => null,
    };

    /// <summary>Lists the applied rows above the code's level that the release may not run on.</summary>
    /// <param name="applied">Every applied row, with its kind as recorded (or filled from the source the image carries).</param>
    /// <param name="expectedLevel">The release's level: its source's highest ordinal.</param>
    /// <param name="release">The release, such as <c>0.5.13</c>, or null when unknown.</param>
    /// <returns>The incompatible rows, empty when the release may run.</returns>
    public static IReadOnlyList<MigrationHistoryEntry> IncompatibleAhead(
        IReadOnlyList<MigrationHistoryEntry> applied, int expectedLevel, string? release)
    {
        var current = MigrationRelease.TryParse(release, out var parsed) ? parsed : null;
        return applied
            .Where(row => row.Ordinal > expectedLevel && !Survives(row, current))
            .ToList();
    }

    /// <summary>Returns the rollback floor: the highest <c>since</c> among applied contracts, as written, or null when none is applied.</summary>
    /// <param name="applied">Every applied row, with its kind.</param>
    public static string? RollbackFloor(IEnumerable<MigrationHistoryEntry> applied)
    {
        MigrationRelease? floor = null;
        foreach (var row in applied)
        {
            if (Parse(row.Kind) == MigrationKind.Contract
                && MigrationRelease.TryParse(row.ContractSince, out var since)
                && (floor is null || since.CompareTo(floor) > 0))
                floor = since;
        }

        return floor?.Text;
    }

    /// <summary>Copies the history with every kind-less row's kind filled from the matching source file, when its checksum still matches.</summary>
    /// <param name="applied">The applied rows.</param>
    /// <param name="source">The image's source migrations by ordinal.</param>
    public static IReadOnlyList<MigrationHistoryEntry> WithSourceKinds(
        IReadOnlyList<MigrationHistoryEntry> applied, IReadOnlyDictionary<int, MigrationDescriptor> source) =>
        applied.Select(row => row.Kind is null
                              && source.TryGetValue(row.Ordinal, out var migration)
                              && migration.Checksum == row.Checksum.Trim()
            ? Copy(row, migration)
            : row).ToList();

    private static MigrationHistoryEntry Copy(MigrationHistoryEntry row, MigrationDescriptor migration) => new()
    {
        Ordinal = row.Ordinal,
        Version = row.Version,
        Name = row.Name,
        Checksum = row.Checksum,
        AppliedAt = row.AppliedAt,
        AppliedBy = row.AppliedBy,
        ExecutionMs = row.ExecutionMs,
        Kind = ToText(migration.Kind),
        ContractSince = migration.ContractSince,
    };

    private static bool Survives(MigrationHistoryEntry row, MigrationRelease? release) => Parse(row.Kind) switch
    {
        MigrationKind.Expand => true,
        MigrationKind.Contract => release is not null
                                  && MigrationRelease.TryParse(row.ContractSince, out var since)
                                  && since.CompareTo(release) <= 0,
        _ => false,
    };
}
