using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;

/// <summary>Delegating handler attaching a cached client-credentials bearer token to outgoing requests lacking an <c>Authorization</c> header.</summary>
internal sealed class OAuth2ClientCredentialsHandler : OAuth2BearerTokenHandler
{
    private readonly string _clientName;
    private readonly IOptionsMonitor<OAuth2ClientCredentialsOptions> _optionsMonitor;

    public OAuth2ClientCredentialsHandler(
        string clientName,
        OAuth2TokenRepository tokenRepository,
        IOptionsMonitor<OAuth2ClientCredentialsOptions> optionsMonitor)
        : base(tokenRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _clientName = clientName;
        _optionsMonitor = optionsMonitor;
    }

    protected override ValueTask<OAuth2TokenRequest> CreateTokenRequestAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OAuth2TokenRequest(
            "OAuth2 client credentials",
            _clientName,
            "client_credentials",
            _optionsMonitor.Get(_clientName)));
}
