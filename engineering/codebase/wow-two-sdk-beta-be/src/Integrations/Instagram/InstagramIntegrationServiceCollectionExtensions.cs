using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Http.Resilience;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Provides opt-in registration for the guarded Instagram provider client.</summary>
public static class InstagramIntegrationServiceCollectionExtensions
{
    private const string ClientName = "WoW.Two.Integrations.Instagram";
    /// <summary>Registers the client with a provider-specific token factory and a single transport attempt per call.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="tokenProviderFactory">Explicitly selects this integration's credential source, independently of other clients.</param>
    /// <param name="configure">Sets transport bounds before the host's Integrations:Instagram section.</param>
    /// <returns>The HTTP builder; do not replace the guarded primary handler or add retry handlers.</returns>
    public static IHttpClientBuilder AddInstagramIntegration(this IServiceCollection services,
        Func<IServiceProvider, IAccessTokenService> tokenProviderFactory,
        Action<InstagramIntegrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tokenProviderFactory);
        services.AddModuleOptions(InstagramIntegrationOptions.SectionName, configure,
            builder => builder.Validate(options => options.IsValid(),
                "Instagram requires the HTTPS Graph origin and valid transport bounds."));
        services.TryAddSingleton(TimeProvider.System);
        return services.AddHttpClient(ClientName, (provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<InstagramIntegrationOptions>().BaseAddress;
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            })
            .AddTypedClient<IInstagramClient>((client, provider) => new InstagramClient(client,
                tokenProviderFactory(provider), provider.GetRequiredService<InstagramIntegrationOptions>(),
                provider.GetRequiredService<TimeProvider>()))
            .AddSdkResilience(options =>
            {
                options.MaxRetryAttempts = 0;
                options.AttemptTimeout = TimeSpan.FromMinutes(1);
                options.TotalRequestTimeout = TimeSpan.FromSeconds(65);
                options.CircuitBreakerSamplingDuration = TimeSpan.FromMinutes(2);
            })
            .AddSafeOutboundHttp(options => options.AllowedHosts.Add("graph.facebook.com"));
    }
}
