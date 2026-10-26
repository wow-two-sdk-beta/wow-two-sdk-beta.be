namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Extends MCP hosting with origin checks: whether a request's <c>Origin</c> may call an endpoint — the DNS-rebinding
/// defence the Streamable HTTP transport requires of a server.</summary>
public static class McpOriginExtensions
{
    /// <summary>Returns whether <paramref name="origin"/> may call the endpoint: the endpoint's own origin or a configured one.</summary>
    /// <param name="origin">The request's <c>Origin</c> header value.</param>
    /// <param name="ownOrigin">The endpoint's own origin, as the request reached it.</param>
    /// <param name="allowedOrigins">The extra browser origins allowed.</param>
    /// <returns><see langword="true"/> for an allowed http or https origin; <see langword="false"/> for any other value, <c>null</c> included.</returns>
    public static bool Allows(string? origin, string ownOrigin, IEnumerable<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);
        string? normalized = NormalizeOrigin(origin);
        return normalized is not null
            && (normalized == NormalizeOrigin(ownOrigin) || allowedOrigins.Any(allowed => NormalizeOrigin(allowed) == normalized));
    }

    /// <summary>Normalizes an origin to <c>scheme://host[:port]</c>, lowercase and without a default port.</summary>
    /// <param name="value">The origin to normalize.</param>
    /// <returns>The normalized origin, or <see langword="null"/> when the value is not an http or https origin — <c>null</c>, a path,
    /// a query, a fragment or user information.</returns>
    public static string? NormalizeOrigin(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Fragment.Length == 0
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}
