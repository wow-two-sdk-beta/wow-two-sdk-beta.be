using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Ef;

/// <summary>Registration helpers for the EF Migrations runner.</summary>
public static class EfMigrationsServiceCollectionExtensions
{
    /// <summary>Registers an <see cref="EfMigrationsBackgroundService{TContext}"/> that runs <c>Database.MigrateAsync</c> at startup for <typeparamref name="TContext"/>.</summary>
    /// <typeparam name="TContext">The database context to migrate.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddEfMigrationsRunner<TContext>(this IServiceCollection services)
        where TContext : DbContext
        => services.AddEfMigrationsRunner<TContext>(static _ => { });

    /// <summary>Registers an <see cref="EfMigrationsBackgroundService{TContext}"/> with custom options, and the context's set in the migrate tree.</summary>
    /// <remarks>The set answers <c>app migrate check</c>, <c>plan</c>, <c>apply</c>, <c>seed</c> and <c>sets</c> under <see cref="EfMigrationsOptions.SetName"/>; every other verb refuses it.</remarks>
    /// <typeparam name="TContext">The database context to migrate.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">A hook to configure the EF migrations runner options.</param>
    public static IServiceCollection AddEfMigrationsRunner<TContext>(
        this IServiceCollection services,
        Action<EfMigrationsOptions> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddValidatedOptions<EfMigrationsOptions>(
            configure,
            builder => builder
                .Validate(options => options.MaxConnectAttempts > 0, "EfMigrationsOptions.MaxConnectAttempts must be positive.")
                .Validate(options => options.ConnectRetryDelay >= TimeSpan.Zero, "EfMigrationsOptions.ConnectRetryDelay must not be negative.")
                .Validate(
                    options => options.SetName == MigrationConstants.DefaultSetName || MigrationConstants.SetNamePattern().IsMatch(options.SetName),
                    "EfMigrationsOptions.SetName must be 'default' or a lowercase identifier."));
        services.AddHostedService<EfMigrationsBackgroundService<TContext>>();
        services.TryAddSingleton<MigrationRunMode>();
        services.AddSingleton<IMigrationStrategySet>(provider => new EfMigrationStrategySet<TContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<EfMigrationsOptions>(),
            provider.GetService<MigrationRunMode>(),
            provider.GetService<IConfiguration>()));
        return services;
    }
}
