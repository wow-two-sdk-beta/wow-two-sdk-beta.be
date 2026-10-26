using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Services;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Service-collection helper disabling the bespoke migrator's startup hook for tests whose schema comes from elsewhere (e.g. EF <c>EnsureCreated</c>).</summary>
public static class NoOpBespokeMigratorExtensions
{
    /// <summary>Replaces the bespoke migrator dialect and runner of every registered set with no-ops so the startup migrate hook touches no database.</summary>
    /// <remarks>Covers the host's set and each named set registered before the call, since a local boot applies every set.</remarks>
    /// <param name="services">The service collection to neuter.</param>
    public static IServiceCollection DisableBespokeMigrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Replace<IMigrationDialect, NoOpMigrationDialect>(services);
        Replace<IMigrationRunnerService, NoOpMigrationRunnerService>(services);
        return services;
    }

    /// <summary>Removes every registration of a service, keyed or not, and registers the no-op under the same keys.</summary>
    /// <typeparam name="TService">The migrator service.</typeparam>
    /// <typeparam name="TNoOp">The no-op implementation.</typeparam>
    /// <param name="services">The service collection to neuter.</param>
    private static void Replace<TService, TNoOp>(IServiceCollection services)
        where TService : class
        where TNoOp : class, TService
    {
        var keys = services
            .Where(descriptor => descriptor.ServiceType == typeof(TService) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey)
            .Distinct()
            .ToList();

        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == typeof(TService))
                services.RemoveAt(index);
        }

        services.AddSingleton<TService, TNoOp>();
        foreach (var key in keys)
            services.AddKeyedSingleton<TService, TNoOp>(key);
    }
}
