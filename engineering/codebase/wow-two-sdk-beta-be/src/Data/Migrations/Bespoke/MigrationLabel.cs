namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents one migration a verb touched or found, by its <c>NNN-name</c> label and kind.</summary>
/// <param name="Label">The <c>NNN-name</c> label.</param>
/// <param name="Kind">The kind, or null for a history row recorded before kinds whose file the image lacks.</param>
public sealed record MigrationLabel(string Label, MigrationKind? Kind)
{
    /// <summary>Labels a source migration.</summary>
    /// <param name="migration">The source migration.</param>
    public static MigrationLabel Of(MigrationDescriptor migration)
    {
        ArgumentNullException.ThrowIfNull(migration);

        return new MigrationLabel(migration.Label, migration.Kind);
    }

    /// <summary>Labels a history row by its recorded kind.</summary>
    /// <param name="row">The history row.</param>
    public static MigrationLabel Of(MigrationHistoryEntry row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new MigrationLabel(row.Label, MigrationCompatibility.Parse(row.Kind));
    }
}
