using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety.Handlers;

internal sealed class ProxyDestinationHandler(
    Uri source,
    OutboundProxyTraffic traffic,
    Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses,
    OutboundProxyLease? lease) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri? target = request.RequestUri;
        if (target is null || !target.IsAbsoluteUri || target.UserInfo.Length != 0
            || target.Scheme != source.Scheme || target.Port != source.Port
            || !string.Equals(target.IdnHost.TrimEnd('.'), source.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
            || !AllowsRequest(request, target))
            throw new OutboundAddressBlockedException(target?.IsAbsoluteUri == true ? target.IdnHost : "(relative)");

        if (traffic == OutboundProxyTraffic.Grpc)
        {
            if (request.Version != HttpVersion.Version20 || request.VersionPolicy != HttpVersionPolicy.RequestVersionExact)
                throw new InvalidOperationException("Proxied gRPC requires HTTP/2 with an exact version policy.");
        }
        else if (request.Version.Major >= 3 || request.VersionPolicy == HttpVersionPolicy.RequestVersionOrHigher)
        {
            throw new InvalidOperationException("Explicit proxy requests require HTTP/1 or HTTP/2 without upgrades.");
        }

        if (request.Headers.Host is not null || request.Headers.Contains("Proxy-Authorization") || request.Headers.Upgrade.Count != 0)
            throw new InvalidOperationException("Explicit proxy requests control the destination host, proxy credentials and protocol.");

        lease?.EnsureActive();
        await SafeOutboundProxy.ResolvePublicAsync(target.IdnHost, resolveAddresses, cancellationToken).ConfigureAwait(false);
        lease?.EnsureActive();
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private bool AllowsRequest(HttpRequestMessage request, Uri target) => traffic switch
    {
        OutboundProxyTraffic.WebRead => request.Method == HttpMethod.Get || request.Method == HttpMethod.Head,
        OutboundProxyTraffic.GraphQl => (request.Method == HttpMethod.Get || request.Method == HttpMethod.Post)
            && target.Fragment.Length == 0 && target.AbsolutePath == source.AbsolutePath,
        OutboundProxyTraffic.Grpc => request.Method == HttpMethod.Post && target.Query.Length == 0 && target.Fragment.Length == 0
            && target.AbsolutePath.StartsWith(source.AbsolutePath, StringComparison.Ordinal)
            && IsIdentifier(target.AbsolutePath[source.AbsolutePath.Length..])
            && IsGrpcContentType(request.Content?.Headers.ContentType?.MediaType),
        _ => false,
    };

    internal static bool IsGrpcServicePath(string path) =>
        path.Length > 2 && path[0] == '/' && path[^1] == '/' && path[1..^1].Split('.').All(IsIdentifier);

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool IsGrpcContentType(string? mediaType) =>
        string.Equals(mediaType, "application/grpc", StringComparison.OrdinalIgnoreCase)
        || mediaType?.StartsWith("application/grpc+", StringComparison.OrdinalIgnoreCase) == true;
}
