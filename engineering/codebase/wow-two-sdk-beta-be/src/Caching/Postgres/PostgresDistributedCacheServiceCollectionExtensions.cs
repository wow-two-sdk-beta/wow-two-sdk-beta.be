using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Jobs.Core;

namespace WoW.Two.Sdk.Beta.Be.Caching.Postgres;

/// <summary>Registers a PostgreSQL table as the host's <see cref="IDistributedCache"/>, with the migration set that creates it and the sweep that empties it.</summary>
public static class PostgresDistributedCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresDistributedCache"/> as the <see cref="IDistributedCache"/>, configured from
    /// <see cref="PostgresDistributedCacheOptions"/> (code, then the host section <c>Caching:Postgres</c>), plus the
    /// <c>distributed_cache</c> migration set and a periodic sweep of expired rows. HybridCache picks it up as its L2.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the connection string, schema, table, default sliding expiration and sweep interval.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    ///   - replaces any <see cref="IDistributedCache"/> registered before; a memory default registered after with <c>TryAdd</c>, as <c>AddDistributedMemoryCache</c> does, never displaces it
    ///   - the host's migrate run applies the set like its own: <c>app migrate</c> on deploy, <c>MigrateBespokeOnStartupAsync</c> on a local run; the cache never creates its table
    ///   - the set owns <see cref="PostgresDistributedCacheOptions.Schema"/> and connects through <see cref="PostgresDistributedCacheOptions.ConnectionString"/>, unless a migrate run's key connects every set
    ///   - the connection string is validated when the host starts; describing the set (<c>app migrate sets</c>) needs none
    ///   - safe to call again: each call adds its <paramref name="configure"/>, the services register once
    /// </remarks>
    public static IServiceCollection AddPostgresDistributedCache(
        this IServiceCollection services,
        Action<PostgresDistributedCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModuleOptions(
            PostgresDistributedCacheOptions.SectionName,
            configure,
            static builder => builder
                .Validate(static o => !string.IsNullOrWhiteSpace(o.ConnectionString), "PostgresDistributedCacheOptions.ConnectionString is required.")
                .Validate(static o => MigrationConstants.SchemaNamePattern().IsMatch(o.Schema), "PostgresDistributedCacheOptions.Schema must be a lowercase identifier (a-z, 0-9 and _, 63 characters at most).")
                .Validate(static o => MigrationConstants.IdentifierPattern().IsMatch(o.TableName), "PostgresDistributedCacheOptions.TableName must be a lowercase identifier (a-z, 0-9 and _, 63 characters at most).")
                .Validate(static o => o.DefaultSlidingExpiration > TimeSpan.Zero, "PostgresDistributedCacheOptions.DefaultSlidingExpiration must be positive.")
                .Validate(static o => o.ExpiredItemsDeletionInterval > TimeSpan.Zero, "PostgresDistributedCacheOptions.ExpiredItemsDeletionInterval must be positive."));

        services.RemoveAll<IDistributedCache>();
        services.AddSingleton<IDistributedCache>(static provider => provider.GetRequiredService<PostgresDistributedCache>());
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(PostgresDistributedCache) && !descriptor.IsKeyedService))
            return services;

        services.TryAddSingleton(TimeProvider.System);
        services.AddKeyedSingleton(PostgresDistributedCacheConstants.MigrationSetName, static (provider, _) =>
            NpgsqlDataSource.Create(provider.GetRequiredService<PostgresDistributedCacheOptions>().ConnectionString));
        services.AddKeyedSingleton<IDbConnectionFactory>(PostgresDistributedCacheConstants.MigrationSetName, static (provider, key) =>
            new DataSourceConnectionFactory(provider.GetRequiredKeyedService<NpgsqlDataSource>(key)));
        services.AddKeyedSingleton(PostgresDistributedCacheConstants.MigrationSetName, static (provider, _) =>
            new DatabaseSettings { ConnectionString = provider.GetRequiredService<PostgresDistributedCacheOptions>().ConnectionString });
        services.AddSingleton(static provider => new PostgresDistributedCache(
            provider.GetRequiredService<PostgresDistributedCacheOptions>(),
            provider.GetRequiredKeyedService<NpgsqlDataSource>(PostgresDistributedCacheConstants.MigrationSetName),
            provider.GetRequiredService<TimeProvider>()));

        // The set reads schema and table without validating the cache's options, so describing it needs no connection string.
        services.AddDatabaseBespokeMigrationSet(
            PostgresDistributedCacheConstants.MigrationSetName,
            static provider => CreateBroker(ReadUnvalidated(provider).TableName),
            static migration =>
            {
                migration.SchemaOwner = PostgresDistributedCacheConstants.SchemaOwner;
                migration.Release = SdkRelease();
            });
        services.AddOptions<MigrationOptions>(PostgresDistributedCacheConstants.MigrationSetName)
            .Configure<IServiceProvider>(static (migration, provider) => migration.SchemaName = ReadUnvalidated(provider).Schema);

        services.AddPeriodicJob<PostgresDistributedCache>(
            PostgresDistributedCacheConstants.SweepJobName,
            static (cache, cancellationToken) => cache.DeleteExpiredItemsAsync(cancellationToken),
            static (job, provider) => job.Interval = provider.GetRequiredService<PostgresDistributedCacheOptions>().ExpiredItemsDeletionInterval);
        return services;
    }

    /// <summary>Builds the set's source: the embedded scripts as written for the default table, else rewritten to the configured one.</summary>
    /// <param name="tableName">The configured table.</param>
    private static IMigrationBroker CreateBroker(string tableName)
    {
        var embedded = new EmbeddedResourceMigrationBroker(
            typeof(PostgresDistributedCache).Assembly, PostgresDistributedCacheConstants.MigrationResourcePrefix);
        return tableName == PostgresDistributedCacheConstants.DefaultTableName
            ? embedded
            : new PostgresDistributedCacheMigrationBroker(embedded, tableName);
    }

    /// <summary>Reads the SDK's release for the set, so a later release's contracts compare against it; null when the version does not parse.</summary>
    private static string? SdkRelease()
    {
        var text = MigrationRelease.TextOf(typeof(PostgresDistributedCache).Assembly);
        return MigrationRelease.TryParse(text, out _) ? text : null;
    }

    /// <summary>Fills the cache's options through every configure and post-configure step, the host section included, without running its rules.</summary>
    /// <param name="provider">The root provider.</param>
    private static PostgresDistributedCacheOptions ReadUnvalidated(IServiceProvider provider)
    {
        var draft = new PostgresDistributedCacheOptions();
        foreach (var setup in provider.GetServices<IConfigureOptions<PostgresDistributedCacheOptions>>())
        {
            if (setup is IConfigureNamedOptions<PostgresDistributedCacheOptions> named)
                named.Configure(Microsoft.Extensions.Options.Options.DefaultName, draft);
            else
                setup.Configure(draft);
        }

        foreach (var setup in provider.GetServices<IPostConfigureOptions<PostgresDistributedCacheOptions>>())
            setup.PostConfigure(Microsoft.Extensions.Options.Options.DefaultName, draft);

        return draft;
    }
}
