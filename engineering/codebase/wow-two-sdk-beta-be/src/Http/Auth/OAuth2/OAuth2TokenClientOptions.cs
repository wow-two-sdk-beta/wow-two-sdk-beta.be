using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Holds what every OAuth2 grant shares: the token endpoint, the client's credentials and the token asked for.</summary>
public abstract record OAuth2TokenClientOptions
{
    /// <summary>Gets or sets the absolute URL of the OAuth2 token endpoint. Required.</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>Gets or sets the OAuth2 client identifier. Required.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Gets or sets the OAuth2 client secret; the client authenticates with it unless <see cref="ClientPrivateKey"/> is set.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Gets or sets the client's PEM private key, RSA or EC, set instead of <see cref="ClientSecret"/>: the client then authenticates with <c>private_key_jwt</c>.</summary>
    public string? ClientPrivateKey { get; set; }

    /// <summary>Gets or sets the <c>kid</c> of <see cref="ClientPrivateKey"/> in the client's registered key set; optional.</summary>
    public string? ClientPrivateKeyId { get; set; }

    /// <summary>Gets or sets the audience of a <c>private_key_jwt</c> assertion, the authorization server's issuer; null uses the origin of <see cref="TokenEndpoint"/>.</summary>
    public string? ClientAssertionAudience { get; set; }

    /// <summary>Gets or sets the RFC 8707 resource indicator sent as the <c>resource</c> form field; optional for client credentials, required for token exchange.</summary>
    public string? Resource { get; set; }

    /// <summary>Gets or sets the optional space-separated scope value sent with the token request.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets the extra form fields for the token request (e.g. <c>audience</c> for Auth0-style servers).</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets how long before the token's expiry a fresh one is fetched. Default 60s.</summary>
    public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Writes the record's text without the secret, the key or the extra fields, so logging the options cannot leak them.</summary>
    /// <param name="builder">The text so far.</param>
    /// <returns><c>true</c>, since members were written.</returns>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"{nameof(TokenEndpoint)} = {TokenEndpoint}, {nameof(ClientId)} = {ClientId}, {nameof(Resource)} = {Resource}, {nameof(Scope)} = {Scope}");
        return true;
    }
}
