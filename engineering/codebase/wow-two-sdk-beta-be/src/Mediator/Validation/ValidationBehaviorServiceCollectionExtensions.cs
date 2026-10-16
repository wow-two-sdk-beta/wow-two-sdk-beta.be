using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Trackers;

namespace WoW.Two.Sdk.Beta.Be.Mediator.Validation;

/// <summary>Provides registration for the validation pipeline behavior.</summary>
public static class ValidationBehaviorServiceCollectionExtensions
{
    /// <summary>Registers the validation pipeline behavior and the scoped advisory tracker it feeds.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddMediatorValidatingInterceptor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IValidationAdvisoryTracker, ValidationAdvisoryTracker>();
        return services.AddMediatorInterceptor(typeof(ValidatingInterceptor<,>));
    }
}
