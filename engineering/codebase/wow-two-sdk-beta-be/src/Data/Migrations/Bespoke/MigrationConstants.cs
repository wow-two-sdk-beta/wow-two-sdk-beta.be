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

    /// <summary>Holds the table, inside an owned schema, whose one row names the set, owner and instance that claimed the schema.</summary>
    public const string SchemaOwnerTableName = "migration_schema_owner";

    /// <summary>Holds the configuration key a host sets to name the instance it claims owned schemas as (environment variable <c>Migrations__OwnerInstance</c>).</summary>
    /// <remarks>Without it, the instance defaults to <c>{applicationName}@{DEPLOY_ENVIRONMENT}</c>, or <c>@local</c> when the runner did not deploy the host.</remarks>
    public const string OwnerInstanceConfigurationKey = "Migrations:OwnerInstance";

    /// <summary>Holds the stamp a baseline records on the history rows it marks as applied and on the schema claim it makes.</summary>
    public const string BaselineAppliedBy = "baseline";

    /// <summary>Holds the configuration key of the migrate key: a connection string that, once a migrate run turns it on, connects every set (environment variable <c>Migrations__ConnectionString</c>).</summary>
    /// <remarks>Read only when the host calls <c>AddMigrationConnectionOverride</c>; a boot never uses it.</remarks>
    public const string ConnectionStringConfigurationKey = "Migrations:ConnectionString";

    /// <summary>Holds the configuration key naming who runs a writing verb, recorded on each event row (environment variable <c>Migrations__By</c>).</summary>
    public const string ByConfigurationKey = "Migrations:By";

    /// <summary>Holds the configuration key naming the job that runs a writing verb, recorded on each event row (environment variable <c>Migrations__Job</c>).</summary>
    public const string JobConfigurationKey = "Migrations:Job";

    /// <summary>Holds the reserved advisory-lock class (<c>w2mg</c>) every set's lock takes in the two-key form, apart from the single-key space app locks use.</summary>
    public const int AdvisoryLockClass = 0x77326D67;

    /// <summary>Holds the folder, beside a set's <c>Migrations</c> folder, that carries its <c>NNN-name.json</c> seed files.</summary>
    public const string SeedsFolderName = "Seeds";

    /// <summary>Holds the metadata key that marks a failure as busy: a lock was not acquired within its bound.</summary>
    public const string BusyMetadataKey = "busy";

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

    /// <summary>Matches a lowercase snake_case identifier of 63 characters at most: a table, column or background-migration name.</summary>
    [GeneratedRegex(@"^[a-z_][a-z0-9_]{0,62}$")]
    public static partial Regex IdentifierPattern();

    /// <summary>Matches a PostgreSQL extension name, such as <c>pg_trgm</c> or <c>uuid-ossp</c>.</summary>
    [GeneratedRegex(@"^[a-z0-9_][a-z0-9_-]{0,62}$")]
    public static partial Regex ExtensionNamePattern();

    /// <summary>Matches a seed file name of the form <c>NNN-name.json</c> (three-digit ordinal, then text).</summary>
    [GeneratedRegex(@"^(\d{3})-(.+)\.json$", RegexOptions.IgnoreCase)]
    public static partial Regex SeedFilePattern();

    /// <summary>Matches <c>{{schema:set}}</c>, the placeholder for the schema another set owns, resolved from the database's owner markers.</summary>
    [GeneratedRegex(@"\{\{schema:([a-z][a-z0-9_]{0,39})\}\}")]
    public static partial Regex SetSchemaPlaceholderPattern();
}
