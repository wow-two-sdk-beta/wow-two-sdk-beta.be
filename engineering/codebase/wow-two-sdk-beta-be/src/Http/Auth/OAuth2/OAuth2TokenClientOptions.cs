namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Holds what every OAuth2 grant shares: the token endpoint, the client's credentials and the token asked for.</summary>
public abstract record OAuth2TokenClientOptions
{
    /// <summary>Gets or sets the absolute URL of the OAuth2 token endpoint. Required.</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>Gets or sets the OAuth2 client identifier. Required.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Gets or sets the OAuth2 client secret. Required.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Gets or sets the RFC 8707 resource indicator sent as the <c>resource</c> form field; optional for client credentials, required for token exchange.</summary>
    public string? Resource { get; set; }

    /// <summary>Gets or sets the optional space-separated scope value sent with the token request.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets the extra form fields for the token request (e.g. <c>audience</c> for Auth0-style servers).</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets how long before the token's expiry a fresh one is fetched. Default 60s.</summary>
    public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromSeconds(60);
}
