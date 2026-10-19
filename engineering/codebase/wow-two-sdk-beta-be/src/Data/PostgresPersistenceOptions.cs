using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Data;

/// <summary>Holds options for the one-call Postgres persistence bundle — where the connection string is resolved from and how the context is configured.</summary>
public sealed record PostgresPersistenceOptions
{
    /// <summary>Gets or sets the configuration key the connection string is read from. Default <c>DatabaseSettings:ConnectionString</c>.</summary>
    public string ConnectionStringConfigKey { get; set; } = "DatabaseSettings:ConnectionString";

    /// <summary>Gets or sets the environment variable that overrides the configured connection string when set. Default <c>DB_CONNECTION</c>.</summary>
    public string ConnectionStringEnvironmentVariable { get; set; } = "DB_CONNECTION";

    /// <summary>
    /// Gets or sets a hook that tunes the bundled bespoke migrator — its version stamp, rollback switch or history
    /// table. Pair it with <see cref="MigrationVersionMapper.ToVersion"/> to stamp applied rows with the product version.
    /// </summary>
    public Action<MigrationOptions>? ConfigureMigrations { get; set; }

    /// <summary>Gets or sets whether parameter values reach EF Core logs. <c>null</c> (default) enables it only on a local run (<c>Development</c> without <c>DEPLOY_ENVIRONMENT</c>).</summary>
    public bool? EnableSensitiveDataLogging { get; set; }

    /// <summary>Gets or sets whether EF Core adds detailed query errors. <c>null</c> (default) enables them only on a local run.</summary>
    public bool? EnableDetailedErrors { get; set; }

    /// <summary>Gets or sets whether queries default to no-tracking. Default <c>false</c>.</summary>
    public bool NoTrackingByDefault { get; set; }

    /// <summary>
    /// Gets or sets a hook that runs last on the context's <see cref="DbContextOptionsBuilder"/>, after the provider and the
    /// SDK defaults, so whatever it sets wins — warnings, a logger factory, or a hard <c>EnableSensitiveDataLogging(false)</c>.
    /// </summary>
    public Action<IServiceProvider, DbContextOptionsBuilder>? ConfigureDbContext { get; set; }
}
