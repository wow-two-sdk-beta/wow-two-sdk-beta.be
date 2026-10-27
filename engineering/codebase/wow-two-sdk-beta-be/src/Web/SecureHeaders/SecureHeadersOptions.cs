namespace WoW.Two.Sdk.Beta.Be.Web.SecureHeaders;

/// <summary>Holds the cross-origin isolation policies the OWASP secure-headers preset emits.</summary>
public sealed record SecureHeadersOptions
{
    /// <summary>Gets or sets whether a response carries <c>Cross-Origin-Opener-Policy: same-origin</c>, which severs the <c>window.opener</c> handle of a cross-origin popup.</summary>
    public bool EnableCrossOriginOpenerPolicy { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the opener policy is <c>same-origin-allow-popups</c> instead of <c>same-origin</c>, which keeps the
    /// <c>window.opener</c> of popups this origin opens — OAuth popups such as Google Identity Services need it. Default off.
    /// </summary>
    public bool AllowOpenerPopups { get; set; }

    /// <summary>Gets or sets whether a response carries <c>Cross-Origin-Embedder-Policy: require-corp</c>, which blocks a cross-origin subresource that ships no CORP opt-in.</summary>
    public bool EnableCrossOriginEmbedderPolicy { get; set; } = true;

    /// <summary>
    /// Gets the path prefixes whose responses carry <c>Cross-Origin-Resource-Policy: cross-origin</c>, for static files
    /// another origin embeds — static files short-circuit before routing, so they carry no endpoint to mark.
    /// </summary>
    public IList<string> CrossOriginEmbeddingPaths { get; } = [];
}
