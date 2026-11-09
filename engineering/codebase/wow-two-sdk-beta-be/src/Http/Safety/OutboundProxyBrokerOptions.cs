namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Configures a broker that allocates and releases approved proxy endpoints over HTTPS.</summary>
public class OutboundProxyBrokerOptions
{
    /// <summary>Gets or sets the HTTPS base URL of the broker.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Gets or sets the API key supplied by the host's secret store.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Gets or sets the header in which the broker receives the key.</summary>
    public string ApiKeyHeader { get; set; } = "X-Api-Key";
    /// <summary>Gets or sets the relative acquire path below the base URL.</summary>
    public string AcquirePath { get; set; } = "leases/acquire";
    /// <summary>Gets or sets the relative release path below the base URL.</summary>
    public string ReleasePath { get; set; } = "leases/release";
    /// <summary>Gets or sets the exact proxy hosts the broker may return.</summary>
    public List<string> AllowedProxyHosts { get; set; } = [];

    internal bool TryGetEndpoints(out Uri acquire, out Uri release)
    {
        acquire = release = null!;
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? root) || !SafeOutboundProxy.IsHttpDestination(root)
            || root.Scheme != Uri.UriSchemeHttps || root.Query.Length != 0 || root.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(ApiKey) || ApiKey.IndexOfAny(['\r', '\n']) >= 0
            || !IsKeyHeader(ApiKeyHeader) || AllowedProxyHosts is not { Count: > 0 }
            || AllowedProxyHosts.Any(host => string.IsNullOrWhiteSpace(host)
                || Uri.CheckHostName(host.TrimEnd('.')) == UriHostNameType.Unknown))
            return false;

        root = new Uri(root.AbsoluteUri.TrimEnd('/') + "/");
        return TryEndpoint(root, AcquirePath, out acquire) && TryEndpoint(root, ReleasePath, out release);
    }

    private static bool IsKeyHeader(string? name) => name is { Length: > 0 and <= 80 }
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        && !name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
        && !name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);

    private static bool TryEndpoint(Uri root, string? relative, out Uri endpoint)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains('\\')
            || relative.Split('/').Contains("..") || !Uri.IsWellFormedUriString(relative, UriKind.Relative)
            || !Uri.TryCreate(root, relative, out Uri? candidate)
            || candidate.GetLeftPart(UriPartial.Authority) != root.GetLeftPart(UriPartial.Authority)
            || !candidate.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)
            || candidate.Query.Length != 0 || candidate.Fragment.Length != 0)
            return false;
        endpoint = candidate;
        return true;
    }
}
