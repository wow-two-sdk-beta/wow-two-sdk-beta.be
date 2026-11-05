using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

/// <summary>Holds configuration for the OAuth2 token-exchange handler; <see cref="OAuth2TokenClientOptions.Resource"/> is required, and a token is shared by one session's calls asking one endpoint for the same client id, resource, scope and additional parameters.</summary>
public sealed record OAuth2TokenExchangeOptions : OAuth2TokenClientOptions;
