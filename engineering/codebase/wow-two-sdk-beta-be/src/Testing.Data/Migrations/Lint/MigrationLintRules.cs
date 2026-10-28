namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

/// <summary>Holds the lint's rule names, as findings report them and suppressions name them.</summary>
/// <remarks>Suppress one rule for one file with a comment and a reason: <c>-- lint-allow: index-concurrently the table is empty until v0.6 ships</c>.</remarks>
public static class MigrationLintRules
{
    /// <summary>Holds the rule that a file above the set's cutoff only expands, unless marked <c>-- @contract since=vX.Y</c> (or the legacy <c>-- contract:</c>).</summary>
    public const string ExpandOnly = "expand-only";

    /// <summary>Holds the rule that an index on an existing table is built <c>CONCURRENTLY</c>.</summary>
    public const string IndexConcurrently = "index-concurrently";

    /// <summary>Holds the rule that a foreign key or check added to an existing table is added <c>NOT VALID</c>.</summary>
    public const string ConstraintNotValid = "constraint-not-valid";

    /// <summary>Holds the rule that an existing column's type is not changed in place, which rewrites the table under an exclusive lock.</summary>
    public const string AlterColumnType = "alter-column-type";

    /// <summary>Holds the rule that a column added to an existing table takes no volatile default, which rewrites the table.</summary>
    public const string VolatileDefault = "volatile-default";

    /// <summary>Holds the rule that a key or a mapping onto another set's table names a table that set publishes, through a declared dependency.</summary>
    public const string PublishedTables = "published-tables";

    /// <summary>Holds the rule that a suppression names a known rule and gives a reason.</summary>
    public const string Suppression = "lint-allow";

    /// <summary>Holds the rule that a set's source reads: folder names and header directives parse.</summary>
    public const string Source = "source";

    /// <summary>Gets every rule a suppression may name.</summary>
    public static IReadOnlySet<string> Suppressible { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ExpandOnly, IndexConcurrently, ConstraintNotValid, AlterColumnType, VolatileDefault, PublishedTables,
    };
}
