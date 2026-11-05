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
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddresses = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(proxy);
        if (!source.IsAbsoluteUri || source.Scheme is not ("http" or "https") || source.UserInfo.Length != 0
            || source.Host.Length == 0 || source.Port <= 0)
            throw new ArgumentException("An HTTP(S) source without URI credentials is required.", nameof(source));
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
        return new HttpClient(new ProxyDestinationHandler(source, resolveAddresses) { InnerHandler = transport })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

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
