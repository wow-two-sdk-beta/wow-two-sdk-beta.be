using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>Provides registration for the person operation tracker.</summary>
public static class PersonOperationServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IPersonOperationTracker"/> as a singleton, unless one is registered.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets how long a signal is remembered; null keeps the default.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddPersonOperations(this IServiceCollection services, Action<PersonOperationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatedOptions(
            configure,
            static builder => builder.Validate(static o => o.SignalMemory >= TimeSpan.Zero, "Person operations: SignalMemory must not be negative."));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPersonOperationTracker, PersonOperationTracker>();
        return services;
    }
}
