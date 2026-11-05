using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;

/// <summary>Holds configuration for the OAuth2 <c>client_credentials</c> handler; a token is shared by every client asking one endpoint for the same client id, resource, scope and additional parameters.</summary>
public sealed record OAuth2ClientCredentialsOptions : OAuth2TokenClientOptions;
