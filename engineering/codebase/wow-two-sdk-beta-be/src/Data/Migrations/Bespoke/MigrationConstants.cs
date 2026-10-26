using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds the file names, directives and folder-name pattern the migrator depends on.</summary>
public static partial class MigrationConstants
{
    /// <summary>Holds the Apply script file name inside each migration folder.</summary>
    public const string ApplyFileName = "Apply.sql";

    /// <summary>Holds the Rollback script file name inside each migration folder.</summary>
    public const string RollbackFileName = "Rollback.sql";

    /// <summary>Holds the directive (as a leading SQL comment) that makes an Apply script run outside a transaction.</summary>
    public const string NoTransactionDirective = "-- @no-transaction";

    /// <summary>Holds the whitespace-stripped form of <see cref="NoTransactionDirective"/> used for header matching.</summary>
    public const string NoTransactionDirectiveCompact = "--@no-transaction";

    /// <summary>Holds the reserved subfolder holding unpromoted dev drafts — never read as a numbered migration.</summary>
    public const string DevFolderName = "Dev";

    /// <summary>Holds the name of the set <c>AddDatabaseBespokeMigrations</c> registers — the host's own migrations.</summary>
    public const string DefaultSetName = "default";

    /// <summary>Holds the placeholder a migration script uses for its set's schema, substituted per dialect after the checksum is taken.</summary>
    /// <remarks>PostgreSQL writes the schema name; SQLite drops <c>{{schema}}.</c> and writes <c>main</c> for a bare placeholder.</remarks>
    public const string SchemaPlaceholder = "{{schema}}";

    /// <summary>Holds the table, inside an owned schema, whose one row names the set and owner that claimed the schema.</summary>
    public const string SchemaOwnerTableName = "migration_schema_owner";

    /// <summary>Matches a migration folder name of the form <c>NNN-name</c> (three-digit ordinal, then text).</summary>
    [GeneratedRegex(@"^(\d{3})-(.+)$")]
    public static partial Regex FolderPattern();

    /// <summary>Matches a named set: lowercase letter first, then lowercase letters, digits or underscores, 40 characters at most.</summary>
    /// <remarks>The name becomes part of the set's default history table, <c>{name}_migration_history</c>.</remarks>
    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,39}$")]
    public static partial Regex SetNamePattern();

    /// <summary>Matches a schema name that is safe to write unquoted: a lowercase PostgreSQL identifier of 63 characters at most.</summary>
    [GeneratedRegex(@"^[a-z_][a-z0-9_]{0,62}$")]
    public static partial Regex SchemaNamePattern();
}
