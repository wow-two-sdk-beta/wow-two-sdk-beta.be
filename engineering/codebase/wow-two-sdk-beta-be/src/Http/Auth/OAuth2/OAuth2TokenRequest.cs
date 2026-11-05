namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Provides one grant to ask of a token endpoint: its grant-specific form fields and the cache key its token lives under.</summary>
internal sealed class OAuth2TokenRequest
{
    /// <summary>Creates the grant, failing loud when the options cannot reach a token endpoint.</summary>
    /// <param name="flowName">The grant's name in error messages.</param>
    /// <param name="clientName">The HTTP client the token is fetched for.</param>
    /// <param name="grantType">The <c>grant_type</c> value.</param>
    /// <param name="options">The endpoint, credentials and token asked for.</param>
    /// <param name="sessionKey">What separates this token from another session's; never a subject token.</param>
    /// <param name="grantFields">The grant's own form fields; these stay out of the cache key.</param>
    public OAuth2TokenRequest(
        string flowName,
        string clientName,
        string grantType,
        OAuth2TokenClientOptions options,
        string? sessionKey = null,
        IReadOnlyList<KeyValuePair<string, string>>? grantFields = null)
    {
        TokenEndpoint = options.TokenEndpoint
            ?? throw new InvalidOperationException($"{flowName} for HTTP client '{clientName}': TokenEndpoint is not configured.");
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException($"{flowName} for HTTP client '{clientName}': ClientId is not configured.");
        }

        FlowName = flowName;
        ClientName = clientName;
        GrantType = grantType;
        Options = options;
        GrantFields = grantFields ?? [];
        CacheKey = BuildCacheKey(grantType, TokenEndpoint, options, sessionKey);
    }

    public string FlowName { get; }

    public string ClientName { get; }

    public string GrantType { get; }

    public Uri TokenEndpoint { get; }

    public OAuth2TokenClientOptions Options { get; }

    public IReadOnlyList<KeyValuePair<string, string>> GrantFields { get; }

    public string CacheKey { get; }

    private static string BuildCacheKey(string grantType, Uri tokenEndpoint, OAuth2TokenClientOptions options, string? sessionKey)
    {
        string[] fields =
        [
            grantType,
            tokenEndpoint.AbsoluteUri,
            options.ClientId,
            sessionKey ?? "",
            string.IsNullOrWhiteSpace(options.Resource) ? "" : options.Resource,
            string.IsNullOrWhiteSpace(options.Scope) ? "" : options.Scope,
        ];
        var parameters = options.AdditionalParameters
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .SelectMany(parameter => new[] { parameter.Key, parameter.Value });

        // Escaping keeps the separator out of every field, so two different requests never join into one key.
        return string.Join('\n', fields.Concat(parameters).Select(Uri.EscapeDataString));
    }
}
