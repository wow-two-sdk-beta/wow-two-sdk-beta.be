using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using WoW.Two.Sdk.Beta.Be.Http.Safety.Handlers;
using WoW.Two.Sdk.Beta.Be.Http.Safety.Validators;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Creates an explicit public proxy transport for a single source origin.</summary>
public static class SafeOutboundProxy
{
    /// <summary>Creates a caller-owned client for GET and HEAD on the source origin.</summary>
    /// <param name="source">An absolute HTTP(S) URI whose origin the client may fetch.</param>
    /// <param name="proxy">The proxy endpoint, without credentials or a path.</param>
    /// <param name="credentials">Optional credentials supplied to the proxy transport.</param>
    /// <param name="resolveAddresses">Optional DNS resolver. The public-address policy applies to all returned addresses.</param>
    /// <returns>A client with explicit proxy routing and a caller-controlled deadline.</returns>
    public static HttpClient CreateClient(
        Uri source,
        Uri proxy,
        NetworkCredential? credentials = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddresses = null) =>
        CreateHttpClient(CreateHandler(source, proxy, OutboundProxyTraffic.WebRead, credentials, resolveAddresses));

    /// <summary>Creates an owned proxy handler for one destination and request policy.</summary>
    /// <param name="destination">The web origin, exact GraphQL endpoint, or HTTPS gRPC service URL ending in a slash.</param>
    /// <param name="proxy">The proxy endpoint without credentials or a path.</param>
    /// <param name="traffic">The requests the destination accepts.</param>
    /// <param name="credentials">Optional credentials for the proxy only.</param>
    /// <param name="resolveAddresses">Optional DNS resolver subject to public-address validation.</param>
    /// <returns>A handler that the caller disposes after its HTTP client or gRPC channel.</returns>
    public static HttpMessageHandler CreateHandler(
        Uri destination,
        Uri proxy,
        OutboundProxyTraffic traffic,
        NetworkCredential? credentials = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddresses = null) =>
        CreateHandler(destination, proxy, traffic, credentials, resolveAddresses, lease: null);

    internal static HttpMessageHandler CreateHandler(
        Uri destination,
        Uri proxy,
        OutboundProxyTraffic traffic,
        NetworkCredential? credentials,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddresses,
        OutboundProxyLease? lease)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(proxy);
        if (!IsHttpDestination(destination) || !Enum.IsDefined(traffic))
            throw new ArgumentException("An HTTP(S) destination and a supported proxy policy are required.", nameof(destination));
        if (traffic != OutboundProxyTraffic.WebRead && (destination.Scheme != Uri.UriSchemeHttps
            || destination.Query.Length != 0 || destination.Fragment.Length != 0))
            throw new ArgumentException("An HTTPS API endpoint without a query or fragment is required.", nameof(destination));
        if (traffic == OutboundProxyTraffic.Grpc && !ProxyDestinationHandler.IsGrpcServicePath(destination.AbsolutePath))
            throw new ArgumentException("A gRPC destination must end with a service path such as /example.Catalog/.", nameof(destination));
        if (!TryParseEndpoint(proxy.OriginalString, out Uri? endpoint))
            throw new ArgumentException("The proxy endpoint is invalid.", nameof(proxy));

        resolveAddresses ??= Dns.GetHostAddressesAsync;
        var transport = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = true,
            Proxy = new WebProxy(endpoint) { Credentials = credentials },
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = (context, cancellationToken) =>
                ConnectAsync(context, endpoint, resolveAddresses, cancellationToken),
        };
        return new ProxyDestinationHandler(destination, traffic, resolveAddresses, lease) { InnerHandler = transport };
    }

    internal static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

    internal static bool IsHttpDestination(Uri? destination) =>
        destination is { IsAbsoluteUri: true, Port: > 0 } && destination.Scheme is "http" or "https"
        && destination.UserInfo.Length == 0 && destination.Host.Length > 0;

    /// <summary>Parses a supported proxy endpoint and rejects URI credentials and nonpublic literal addresses.</summary>
    /// <param name="value">The configured endpoint.</param>
    /// <param name="endpoint">The parsed endpoint when valid.</param>
    /// <returns>Whether the endpoint is valid. DNS addresses are checked at connection time.</returns>
    public static bool TryParseEndpoint(string? value, [NotNullWhen(true)] out Uri? endpoint)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            && parsed.Scheme is "http" or "https" or "socks4" or "socks4a" or "socks5"
            && parsed.UserInfo.Length == 0 && parsed.Host.Length > 0 && parsed.Port is > 0 and <= 65535
            && parsed.Query.Length == 0 && parsed.Fragment.Length == 0 && parsed.AbsolutePath is "" or "/"
            && (!IPAddress.TryParse(parsed.IdnHost.Trim('[', ']'), out IPAddress? address)
                || new OutboundAddressValidator().IsAllowed(address)))
        {
            endpoint = parsed;
            return true;
        }

        endpoint = null;
        return false;
    }

    internal static async Task<IPAddress[]> ResolvePublicAsync(
        string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await resolveAddresses(host, cancellationToken).ConfigureAwait(false);
        var validator = new OutboundAddressValidator();
        if (addresses.Length == 0 || addresses.Any(address => !validator.IsAllowed(address)))
            throw new OutboundAddressBlockedException(host);

        return addresses;
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        Uri proxy,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses,
        CancellationToken cancellationToken)
    {
        DnsEndPoint target = context.DnsEndPoint;
        if (target.Port != proxy.Port || !string.Equals(
            target.Host.TrimEnd('.'), proxy.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
            throw new OutboundAddressBlockedException(target.Host);

        IPAddress[] addresses = await ResolvePublicAsync(target.Host, resolveAddresses, cancellationToken).ConfigureAwait(false);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, target.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
