using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Registers the guarded HTTP transport used for proxy lease acquisition.</summary>
public static class OutboundProxyServiceCollectionExtensions
{
    /// <summary>Registers <see cref="OutboundProxyBroker"/> with an explicit public HTTPS control connection.</summary>
    /// <param name="services">The application's service collection.</param>
    public static IServiceCollection AddOutboundProxyBroker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboundProxyBroker>();
        services.AddHttpClient(OutboundProxyBroker.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddSafeOutboundHttp();
        return services;
    }
}
