using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.Parsers;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Provides registration extensions for the SQL migrator — the host's default set and each module's named set, from embedded resources or the filesystem.</summary>
public static class MigrationServiceCollectionExtensions
{
    /// <summary>Holds the host-configuration key of the application name (the generic host's <c>HostDefaults.ApplicationKey</c>).</summary>
    private const string ApplicationNameKey = "applicationName";

    /// <summary>Holds the variable the deploy runner sets on every deployed container.</summary>
    private const string DeployEnvironmentKey = "DEPLOY_ENVIRONMENT";

    /// <summary>Holds the host-configuration key of the environment name (the generic host's <c>HostDefaults.EnvironmentKey</c>).</summary>
    private const string EnvironmentKey = "environment";

    /// <summary>Holds the environment name of a developer's machine.</summary>
    private const string DevelopmentEnvironment = "Development";

    /// <summary>Adds the embedded-resource SQL migrator (the runtime default — schema ships in the binary).</summary>
    /// <remarks>The host passes the assembly that embeds the <c>Migrations/NNN-name/*.sql</c> resources; requires an <see cref="IDbConnectionFactory"/> registered alongside.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="sqlAssembly">The assembly that embeds the migration SQL resources.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/> (e.g. enable rollback for dev hosts).</param>
    public static IServiceCollection AddDatabaseBespokeMigrations(
        this IServiceCollection services, Assembly sqlAssembly, Action<MigrationOptions>? configure = null) =>
        services.AddDatabaseBespokeMigrations(_ => new EmbeddedResourceMigrationBroker(sqlAssembly), configure);

    /// <summary>Adds the filesystem SQL migrator over an on-disk migrations root (the CLI and dev default — schema is edited live).</summary>
    /// <remarks>Wire from the CLI host; reads <c>{root}/NNN-name/{Apply,Rollback}.sql</c> and requires an <see cref="IDbConnectionFactory"/> registered alongside.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="migrationsRoot">The on-disk folder containing the <c>NNN-name</c> migration directories.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/> (e.g. enable rollback for dev hosts).</param>
    public static IServiceCollection AddDatabaseBespokeMigrations(
        this IServiceCollection services, string migrationsRoot, Action<MigrationOptions>? configure = null) =>
        services.AddDatabaseBespokeMigrations(_ => new FileSystemMigrationBroker(migrationsRoot), configure);

    /// <summary>Adds a named migration set from an assembly's embedded <c>Migrations/NNN-name/*.sql</c> resources — a module's schema beside the host's own.</summary>
    /// <remarks>See <see cref="AddDatabaseBespokeMigrationSet(IServiceCollection, string, Func{IServiceProvider, IMigrationBroker}, Action{MigrationOptions}?)"/>.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="setName">The set name: a lowercase identifier other than <see cref="MigrationConstants.DefaultSetName"/>.</param>
    /// <param name="sqlAssembly">The assembly that embeds the set's SQL resources.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    public static IServiceCollection AddDatabaseBespokeMigrationSet(
        this IServiceCollection services, string setName, Assembly sqlAssembly, Action<MigrationOptions>? configure = null) =>
        services.AddDatabaseBespokeMigrationSet(setName, _ => new EmbeddedResourceMigrationBroker(sqlAssembly), configure);

    /// <summary>Adds a named migration set read from an on-disk migrations root.</summary>
    /// <remarks>See <see cref="AddDatabaseBespokeMigrationSet(IServiceCollection, string, Func{IServiceProvider, IMigrationBroker}, Action{MigrationOptions}?)"/>.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="setName">The set name: a lowercase identifier other than <see cref="MigrationConstants.DefaultSetName"/>.</param>
    /// <param name="migrationsRoot">The on-disk folder containing the set's <c>NNN-name</c> directories.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    public static IServiceCollection AddDatabaseBespokeMigrationSet(
        this IServiceCollection services, string setName, string migrationsRoot, Action<MigrationOptions>? configure = null) =>
        services.AddDatabaseBespokeMigrationSet(setName, _ => new FileSystemMigrationBroker(migrationsRoot), configure);

    /// <summary>Adds a named migration set over a caller-supplied source, such as an <see cref="EmbeddedResourceMigrationBroker"/> with a per-dialect prefix.</summary>
    /// <remarks>
    ///   - the set keeps its own history (<c>{setName}_migration_history</c> by default) and advisory lock (derived from the name)
    ///   - set <see cref="MigrationOptions.SchemaName"/> and <see cref="MigrationOptions.SchemaOwner"/> for a set that owns its schema; <see cref="MigrationOptions.OwnerInstance"/> defaults from configuration
    ///   - the set uses a keyed <see cref="IDbConnectionFactory"/> registered under <paramref name="setName"/> when one exists, else the host's
    ///   - each service registers keyed by <paramref name="setName"/>; <see cref="IMigrationSetRegistry"/> lists every set
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="setName">The set name: a lowercase identifier other than <see cref="MigrationConstants.DefaultSetName"/>.</param>
    /// <param name="brokerFactory">A factory that builds the set's migration source.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    /// <exception cref="ArgumentException">The name is reserved or not a lowercase identifier.</exception>
    /// <exception cref="InvalidOperationException">A set with that name is already registered.</exception>
    public static IServiceCollection AddDatabaseBespokeMigrationSet(
        this IServiceCollection services,
        string setName,
        Func<IServiceProvider, IMigrationBroker> brokerFactory,
        Action<MigrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(brokerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(setName);

        if (setName == MigrationConstants.DefaultSetName || !MigrationConstants.SetNamePattern().IsMatch(setName))
        {
            throw new ArgumentException(
                $"Migration set name '{setName}' must be a lowercase identifier (a-z first, then a-z, 0-9 and _, 40 characters at most) " +
                $"other than '{MigrationConstants.DefaultSetName}', which AddDatabaseBespokeMigrations registers.", nameof(setName));
        }

        if (IsRegistered(services, setName))
            throw new InvalidOperationException($"Migration set '{setName}' is already registered.");

        services.AddSingleton(new MigrationSetRegistration(setName));

        // Set defaults first, so the caller's hook can still override the table and lock.
        var builder = services.AddOptions<MigrationOptions>(setName).Configure(options =>
        {
            options.SetName = setName;
            options.TableName = $"{setName}_migration_history";
            options.AdvisoryLockId = DeriveAdvisoryLockId(setName);
        });
        if (configure is not null)
            builder.Configure(configure);
        ApplyDefaults(builder, provider => provider.GetKeyedService<IMigrationBroker>(setName));
        ApplyRules(builder);
        builder.ValidateOnStart();

        services.AddKeyedSingleton(setName, (provider, _) =>
            provider.GetRequiredService<IOptionsMonitor<MigrationOptions>>().Get(setName));
        services.AddKeyedSingleton(setName, (provider, _) => brokerFactory(provider));
        services.AddKeyedSingleton(setName, (provider, _) =>
            CreateDialect(provider.GetRequiredKeyedService<MigrationOptions>(setName).Provider));
        services.AddKeyedSingleton<IMigrationHistoryRepository>(setName, (provider, _) => new MigrationHistoryRepository(
            provider.GetRequiredKeyedService<IMigrationDialect>(setName), provider.GetRequiredKeyedService<MigrationOptions>(setName)));
        services.AddKeyedSingleton<IMigrationRunnerService>(setName, (provider, _) => CreateRunner(
            provider,
            provider.GetRequiredKeyedService<IMigrationBroker>(setName),
            provider.GetRequiredKeyedService<IMigrationHistoryRepository>(setName),
            provider.GetRequiredKeyedService<MigrationOptions>(setName),
            provider.GetRequiredKeyedService<IMigrationDialect>(setName),
            provider.GetKeyedService<IDbConnectionFactory>(setName)));
        AddShared(services);

        return services;
    }

    /// <summary>Adds the default set over a caller-supplied source factory — the shared core both public overloads and the CLI delegate to.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="brokerFactory">A factory that builds the migration broker from the provider.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/>.</param>
    internal static IServiceCollection AddDatabaseBespokeMigrations(
        this IServiceCollection services, Func<IServiceProvider, IMigrationBroker> brokerFactory, Action<MigrationOptions>? configure)
    {
        services.AddValidatedOptions(configure, builder =>
        {
            ApplyDefaults(builder, provider => provider.GetService<IMigrationBroker>());
            ApplyRules(builder);
        });

        services.AddSingleton(brokerFactory);
        // Resolved rather than captured, so the dialect reads the provider AFTER Configure has run.
        services.AddSingleton<IMigrationDialect>(serviceProvider =>
            CreateDialect(serviceProvider.GetRequiredService<MigrationOptions>().Provider));
        services.AddSingleton<IMigrationHistoryRepository, MigrationHistoryRepository>();
        services.AddSingleton<IMigrationRunnerService>(provider => CreateRunner(
            provider,
            provider.GetRequiredService<IMigrationBroker>(),
            provider.GetRequiredService<IMigrationHistoryRepository>(),
            provider.GetRequiredService<MigrationOptions>(),
            provider.GetRequiredService<IMigrationDialect>(),
            keyedConnections: null));
        if (!IsRegistered(services, MigrationConstants.DefaultSetName))
            services.AddSingleton(new MigrationSetRegistration(MigrationConstants.DefaultSetName));
        AddShared(services);

        return services;
    }

    /// <summary>Turns on the migrate key for this host at every run: every set connects through <see cref="MigrationConstants.ConnectionStringConfigurationKey"/> when it is set.</summary>
    /// <remarks>
    ///   - a host needs no call: every set registration keeps the key dormant, and the <c>migrate</c> command turns it on for its run (<see cref="MigrationRunMode"/>)
    ///   - call it only where a process outside the command must use the key, such as a test; a boot keeps the service's own connections
    ///   - the string comes from <paramref name="connectionString"/>, else configuration, else the process variable <c>Migrations__ConnectionString</c>
    ///   - blank everywhere leaves every set on its keyed or host connection
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The migrate key's connection string, or null to read it from configuration.</param>
    public static IServiceCollection AddMigrationConnectionOverride(this IServiceCollection services, string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<MigrationConnectionOverride>();
        services.AddSingleton(provider => new MigrationConnectionOverride(ReadMigrateKey(provider, connectionString)));
        return services;
    }

    /// <summary>Reads the migrate key: the given string, else configuration, else the process variable <c>Migrations__ConnectionString</c>.</summary>
    /// <param name="provider">The root provider.</param>
    /// <param name="connectionString">An explicit connection string, or null.</param>
    private static string? ReadMigrateKey(IServiceProvider provider, string? connectionString) =>
        NonBlank(connectionString)
        ?? NonBlank(provider.GetService<IConfiguration>()?[MigrationConstants.ConnectionStringConfigurationKey])
        ?? NonBlank(Environment.GetEnvironmentVariable(EnvironmentVariableOf(MigrationConstants.ConnectionStringConfigurationKey)));

    /// <summary>Registers the services every set shares: hasher, statement parser, clock, audit identity, the set registry and the verb engine.</summary>
    /// <param name="services">The service collection to configure.</param>
    private static void AddShared(IServiceCollection services)
    {
        services.TryAddSingleton<IMigrationChecksumHasher, MigrationChecksumHasher>();
        services.TryAddSingleton<ISqlStatementParser, SqlStatementParser>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMigrationSetRegistry, MigrationSetRegistry>();
        services.TryAddSingleton<IMigrationEngine, MigrationEngine>();

        // The migrate key stays dormant until a migrate command turns the run mode on; AddMigrationConnectionOverride replaces it.
        services.TryAddSingleton<MigrationRunMode>();
        services.TryAddSingleton(provider => new MigrationConnectionOverride(
            provider.GetRequiredService<MigrationRunMode>().IsMigrateCommand ? ReadMigrateKey(provider, null) : null));

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IPostConfigureOptions<MigrationAuditOptions>)))
        {
            services.AddOptions<MigrationAuditOptions>().PostConfigure<IServiceProvider>((audit, provider) =>
            {
                var configuration = provider.GetService<IConfiguration>();
                audit.By ??= NonBlank(configuration?[MigrationConstants.ByConfigurationKey])
                    ?? NonBlank(Environment.GetEnvironmentVariable(EnvironmentVariableOf(MigrationConstants.ByConfigurationKey)))
                    ?? $"{Environment.UserName}@{Environment.MachineName}";
                audit.Job ??= NonBlank(configuration?[MigrationConstants.JobConfigurationKey])
                    ?? NonBlank(Environment.GetEnvironmentVariable(EnvironmentVariableOf(MigrationConstants.JobConfigurationKey)));
            });
        }
    }

    /// <summary>Builds one set's runner, connecting through the migrate key when a migrate run turned it on, else the set's keyed connection, else the host's.</summary>
    private static MigrationRunnerService CreateRunner(
        IServiceProvider provider, IMigrationBroker broker, IMigrationHistoryRepository history, MigrationOptions options,
        IMigrationDialect dialect, IDbConnectionFactory? keyedConnections) => new(
        broker,
        history,
        provider.GetService<MigrationConnectionOverride>() is { IsActive: true } migrateKey
            ? migrateKey.GetFactory(options.Provider)
            : keyedConnections ?? provider.GetRequiredService<IDbConnectionFactory>(),
        options,
        provider.GetRequiredService<ILogger<MigrationRunnerService>>(),
        provider.GetRequiredService<IMigrationChecksumHasher>(),
        provider.GetRequiredService<ISqlStatementParser>(),
        provider.GetRequiredService<TimeProvider>(),
        dialect,
        provider.GetService<IOptions<MigrationAuditOptions>>());

    /// <summary>Fills what the caller left unset from the host, after every configure hook has run: owner instance, release and local run.</summary>
    /// <param name="builder">The options builder of one set.</param>
    /// <param name="broker">Resolves the set's source, whose embedding assembly names the release.</param>
    private static void ApplyDefaults(OptionsBuilder<MigrationOptions> builder, Func<IServiceProvider, IMigrationBroker?> broker) =>
        builder.PostConfigure<IServiceProvider>((options, provider) =>
        {
            var configuration = provider.GetService<IConfiguration>();
            options.OwnerInstance ??= DeriveOwnerInstance(configuration);
            options.LocalRun ??= DeriveLocalRun(configuration);
            if (options.Release is null && broker(provider) is EmbeddedResourceMigrationBroker embedded)
                options.Release = MigrationRelease.TextOf(embedded.Assembly);
        });

    /// <summary>Derives whether the host runs locally: <c>Development</c> without <c>DEPLOY_ENVIRONMENT</c>, read from configuration or the process.</summary>
    /// <param name="configuration">The host configuration, or null when the host registers none.</param>
    internal static bool DeriveLocalRun(IConfiguration? configuration)
    {
        var environment = NonBlank(configuration?[EnvironmentKey])
            ?? NonBlank(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"))
            ?? NonBlank(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
        var deployed = NonBlank(configuration?[DeployEnvironmentKey]) ?? NonBlank(Environment.GetEnvironmentVariable(DeployEnvironmentKey));
        return string.Equals(environment, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase) && deployed is null;
    }

    /// <summary>Spells a configuration key as the process variable that sets it, such as <c>Migrations__By</c>.</summary>
    /// <param name="key">The configuration key.</param>
    private static string EnvironmentVariableOf(string key) => key.Replace(":", "__", StringComparison.Ordinal);

    /// <summary>Derives the instance a host claims owned schemas as: its configured value, else <c>{application}@{deploy environment}</c>.</summary>
    /// <remarks>
    ///   - the application is configuration <c>applicationName</c> (the host's), else the entry assembly's name
    ///   - the environment is <c>DEPLOY_ENVIRONMENT</c> from configuration or the process, else <c>local</c>
    ///   - replicas of one image and configuration derive one value; two instances of one image must configure their own
    /// </remarks>
    /// <param name="configuration">The host configuration, or null when the host registers none.</param>
    internal static string DeriveOwnerInstance(IConfiguration? configuration)
    {
        if (NonBlank(configuration?[MigrationConstants.OwnerInstanceConfigurationKey]) is { } configured)
            return configured;

        var application = NonBlank(configuration?[ApplicationNameKey]) ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "host";
        var environment = NonBlank(configuration?[DeployEnvironmentKey])
            ?? NonBlank(Environment.GetEnvironmentVariable(DeployEnvironmentKey))
            ?? "local";
        return $"{application}@{environment}";
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>States the rules every set's options must satisfy.</summary>
    /// <param name="builder">The options builder of one set.</param>
    private static void ApplyRules(OptionsBuilder<MigrationOptions> builder) => builder
        .Validate(options => !string.IsNullOrWhiteSpace(options.Version), "MigrationOptions.Version must not be empty.")
        .Validate(options => Enum.IsDefined(options.Provider), "MigrationOptions.Provider must be a defined database provider.")
        .Validate(options => Enum.IsDefined(options.TransactionScope), "MigrationOptions.TransactionScope must be a defined transaction scope.")
        .Validate(options => !string.IsNullOrWhiteSpace(options.SchemaName), "MigrationOptions.SchemaName must not be empty.")
        .Validate(options => !string.IsNullOrWhiteSpace(options.TableName), "MigrationOptions.TableName must not be empty.")
        .Validate(options => Enum.IsDefined(options.LevelMismatch), "MigrationOptions.LevelMismatch must be a defined level policy.")
        .Validate(
            options => options.SchemaOwner is null || (!string.IsNullOrWhiteSpace(options.SchemaOwner) && options.SchemaOwner.Length <= 120),
            "MigrationOptions.SchemaOwner must be null or 1 to 120 characters.")
        .Validate(
            options => options.OwnerInstance is null || (!string.IsNullOrWhiteSpace(options.OwnerInstance) && options.OwnerInstance.Length <= 200),
            "MigrationOptions.OwnerInstance must be null or 1 to 200 characters.")
        .Validate(
            options => options.SchemaOwner is null || MigrationConstants.SchemaNamePattern().IsMatch(options.SchemaName),
            "A set with a SchemaOwner needs a SchemaName that is a lowercase identifier (a-z, 0-9 and _, 63 characters at most).")
        .Validate(options => options.Release is null || MigrationRelease.TryParse(options.Release, out _),
            "MigrationOptions.Release must be a version such as 0.5.13 or v0.5.")
        .Validate(options => options.LockTimeout > TimeSpan.Zero, "MigrationOptions.LockTimeout must be positive.")
        .Validate(options => options.LockAttempts >= 1, "MigrationOptions.LockAttempts must be at least 1.")
        .Validate(options => options.LockRetryDelay >= TimeSpan.Zero, "MigrationOptions.LockRetryDelay must not be negative.")
        .Validate(options => options.AdvisoryLockWait >= TimeSpan.Zero, "MigrationOptions.AdvisoryLockWait must not be negative.")
        .Validate(
            options => options.Dependencies.All(dependency => dependency.Level >= 1
                && (dependency.Set == MigrationConstants.DefaultSetName || MigrationConstants.SetNamePattern().IsMatch(dependency.Set))),
            "MigrationOptions.Dependencies must name sets by their lowercase set name, each at a level of 1 or more.")
        .Validate(
            options => options.RequiredExtensions.All(extension => MigrationConstants.ExtensionNamePattern().IsMatch(extension)),
            "MigrationOptions.RequiredExtensions must hold extension names such as pg_trgm or uuid-ossp.")
        .Validate(
            options => options.ReferenceableTables.All(table => MigrationConstants.IdentifierPattern().IsMatch(table.Table)
                && table.Keys.Count > 0
                && table.Keys.Concat(table.ReadableColumns).All(column => MigrationConstants.IdentifierPattern().IsMatch(column))),
            "MigrationOptions.ReferenceableTables must name snake_case tables, each with at least one snake_case key column.");

    /// <summary>Returns whether a set with this name is already registered.</summary>
    /// <param name="services">The service collection to inspect.</param>
    /// <param name="setName">The set name.</param>
    private static bool IsRegistered(IServiceCollection services, string setName) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(MigrationSetRegistration)
            && !descriptor.IsKeyedService
            && descriptor.ImplementationInstance is MigrationSetRegistration registration
            && registration.Name == setName);

    /// <summary>Derives a stable advisory-lock id from a set name (64-bit FNV-1a), so named sets never share the default lock.</summary>
    /// <param name="setName">The set name.</param>
    private static long DeriveAdvisoryLockId(string setName)
    {
        var hash = 14_695_981_039_346_656_037UL;
        foreach (var value in Encoding.UTF8.GetBytes("wow2-migration-set:" + setName))
        {
            hash ^= value;
            hash *= 1_099_511_628_211UL;
        }

        return unchecked((long)hash);
    }

    /// <summary>Selects the dialect implementation for the configured provider.</summary>
    /// <param name="provider">The database provider to resolve a dialect for.</param>
    /// <exception cref="ArgumentOutOfRangeException">The provider has no registered dialect.</exception>
    private static IMigrationDialect CreateDialect(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Postgres => new PostgresMigrationDialect(),
        DatabaseProvider.Sqlite => new SqliteMigrationDialect(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No migration dialect for this provider."),
    };
}
