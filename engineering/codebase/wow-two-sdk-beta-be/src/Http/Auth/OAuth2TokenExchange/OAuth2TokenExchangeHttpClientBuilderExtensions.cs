using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

/// <summary>Provides OAuth2 token exchange (RFC 8693) for an outbound HTTP client calling on a person's behalf.</summary>
public static class OAuth2TokenExchangeHttpClientBuilderExtensions
{
    /// <summary>Adds a handler that exchanges the person's token from <see cref="IOAuth2SubjectTokenSource"/>, caches the result per session (refreshing <see cref="OAuth2TokenClientOptions.RefreshSkew"/> before expiry, and evicting on a <c>401</c>), and sends it as <c>Authorization: Bearer</c>; requests already carrying that header pass through.</summary>
    /// <remarks>Register an own <see cref="IOAuth2SubjectTokenSource"/> to read the person from elsewhere than the current request's bearer token.</remarks>
    /// <param name="builder">The HTTP client builder to extend.</param>
    /// <param name="configure">Configures the token endpoint, client credentials, resource, and refresh behavior.</param>
    public static IHttpClientBuilder AddOAuth2TokenExchange(
        this IHttpClientBuilder builder,
        Action<OAuth2TokenExchangeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(builder.Name, configure);
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<IOAuth2SubjectTokenSource, HttpContextBearerSubjectTokenSource>();
        builder.Services.TryAddSingleton(static sp => new OAuth2TokenRepository(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return builder.AddHttpMessageHandler(sp => new OAuth2TokenExchangeHandler(
            builder.Name,
            sp.GetRequiredService<OAuth2TokenRepository>(),
            sp.GetRequiredService<IOAuth2SubjectTokenSource>(),
            sp.GetRequiredService<IOptionsMonitor<OAuth2TokenExchangeOptions>>()));
    }
}
