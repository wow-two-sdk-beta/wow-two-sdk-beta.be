using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Backend.Beta.Foundation.Options;

namespace WoW.Two.Sdk.Backend.Beta.Web.Antiforgery;

/// <summary>Provides registration for the action-header write guard.</summary>
public static class ActionHeaderServiceCollectionExtensions
{
    /// <summary>Registers the header <see cref="RequireActionHeaderAttribute"/> reads; without this call it reads <c>X-Requested-Action</c>.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the header name; the host section <c>Web:ActionHeader</c> is applied after it.</param>
    public static IServiceCollection AddActionHeaderGuard(
        this IServiceCollection services,
        Action<ActionHeaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddModuleOptions(
            ActionHeaderOptions.SectionName,
            configure,
            builder => builder.Validate(o => !string.IsNullOrWhiteSpace(o.HeaderName), "ActionHeaderOptions.HeaderName must not be empty."));
    }
}
