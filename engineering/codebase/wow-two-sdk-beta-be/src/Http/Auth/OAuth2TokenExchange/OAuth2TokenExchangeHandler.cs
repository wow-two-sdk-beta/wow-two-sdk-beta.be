using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

/// <summary>Delegating handler exchanging the person's token (RFC 8693) for a cached bearer token for one resource, attached to outgoing requests lacking an <c>Authorization</c> header.</summary>
internal sealed class OAuth2TokenExchangeHandler : OAuth2BearerTokenHandler
{
    private const string FlowName = "OAuth2 token exchange";
    private const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private readonly string _clientName;
    private readonly IOAuth2SubjectTokenSource _subjectTokenSource;
    private readonly IOptionsMonitor<OAuth2TokenExchangeOptions> _optionsMonitor;

    public OAuth2TokenExchangeHandler(
        string clientName,
        OAuth2TokenRepository tokenRepository,
        IOAuth2SubjectTokenSource subjectTokenSource,
        IOptionsMonitor<OAuth2TokenExchangeOptions> optionsMonitor)
        : base(tokenRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(subjectTokenSource);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _clientName = clientName;
        _subjectTokenSource = subjectTokenSource;
        _optionsMonitor = optionsMonitor;
    }

    protected override async ValueTask<OAuth2TokenRequest> CreateTokenRequestAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.Get(_clientName);
        if (string.IsNullOrWhiteSpace(options.Resource))
        {
            throw new InvalidOperationException($"{FlowName} for HTTP client '{_clientName}': Resource is not configured.");
        }

        var subject = await _subjectTokenSource.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"{FlowName} for HTTP client '{_clientName}': no person is on this call, so there is no subject token. Jobs with no person use AddOAuth2ClientCredentials.");

        // An empty session key would hand every person one shared token.
        if (string.IsNullOrWhiteSpace(subject.Token) || string.IsNullOrWhiteSpace(subject.SessionKey))
        {
            throw new InvalidOperationException(
                $"{FlowName} for HTTP client '{_clientName}': the subject token source returned an empty Token or SessionKey.");
        }

        return new OAuth2TokenRequest(
            FlowName,
            _clientName,
            GrantType,
            options,
            subject.SessionKey,
            [new("subject_token", subject.Token), new("subject_token_type", AccessTokenType)]);
    }
}
