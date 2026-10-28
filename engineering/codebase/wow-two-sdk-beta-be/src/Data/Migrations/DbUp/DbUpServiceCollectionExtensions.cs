using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.DbUp;

/// <summary>Registration helpers for the DbUp runner.</summary>
public static class DbUpServiceCollectionExtensions
{
    /// <summary>Registers the DbUp background service against <paramref name="connectionString"/>, and the journal's set in the migrate tree.</summary>
    /// <remarks>The set answers <c>app migrate check</c>, <c>plan</c>, <c>apply</c>, <c>seed</c> and <c>sets</c> under <see cref="DbUpOptions.SetName"/>; every other verb refuses it.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The connection string for the target database.</param>
    /// <param name="configure">A hook to configure the rest of the DbUp runner options.</param>
    public static IServiceCollection AddDbUpRunner(
        this IServiceCollection services,
        string connectionString,
        Action<DbUpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var dbUpOptions = new DbUpOptions { ConnectionString = connectionString };
        configure?.Invoke(dbUpOptions);
        services.TryAddSingleton(dbUpOptions);
        services.AddHostedService<DbUpBackgroundService>();
        services.TryAddSingleton<MigrationRunMode>();
        services.AddSingleton<IMigrationStrategySet>(provider => new DbUpMigrationStrategySet(
            dbUpOptions, provider.GetService<MigrationRunMode>(), provider.GetService<IConfiguration>()));
        return services;
    }
}
