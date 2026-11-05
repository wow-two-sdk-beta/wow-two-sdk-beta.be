using System.Net;
using System.Net.Http.Headers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Provides the delegating handler behaviour every grant shares: attach a cached bearer token to requests lacking <c>Authorization</c>, and evict it when the resource answers <c>401</c>.</summary>
internal abstract class OAuth2BearerTokenHandler : DelegatingHandler
{
    private readonly OAuth2TokenRepository _tokenRepository;

    protected OAuth2BearerTokenHandler(OAuth2TokenRepository tokenRepository)
    {
        ArgumentNullException.ThrowIfNull(tokenRepository);
        _tokenRepository = tokenRepository;
    }

    /// <summary>Builds the grant for this call.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The grant to ask of the token endpoint.</returns>
    protected abstract ValueTask<OAuth2TokenRequest> CreateTokenRequestAsync(CancellationToken cancellationToken);

    protected sealed override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers.Authorization is not null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var tokenRequest = await CreateTokenRequestAsync(cancellationToken).ConfigureAwait(false);
        // The HttpClient pipeline reads only exceptions, so the failure crosses back here.
        var token = (await _tokenRepository.GetAccessTokenAsync(tokenRequest, cancellationToken).ConfigureAwait(false))
            .ValueOrThrow();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokenRepository.Evict(tokenRequest.CacheKey, token);
        }

        return response;
    }
}
