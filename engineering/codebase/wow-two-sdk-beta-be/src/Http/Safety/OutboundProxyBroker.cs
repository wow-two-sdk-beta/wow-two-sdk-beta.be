using System.Net.Http.Json;
using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Resolves fixed proxy profiles and acquires routes from configured proxy lease brokers.</summary>
/// <param name="clients">The HTTP client factory configured by <see cref="OutboundProxyServiceCollectionExtensions.AddOutboundProxyBroker"/>.</param>
/// <param name="clock">The clock used to validate the expiry of leased routes.</param>
public sealed class OutboundProxyBroker(IHttpClientFactory clients, TimeProvider clock)
{
    internal const string ClientName = "OutboundProxyBroker";
    private const int MaximumLeaseBytes = 64 * 1024;

    /// <summary>Acquires one proxy for a configured destination.</summary>
    /// <param name="profile">The connection profile chosen by the route.</param>
    /// <param name="destination">The destination the acquired handler will guard.</param>
    /// <param name="routeKey">The non-secret identifier sent to the broker.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    public async ValueTask<OutboundProxyLease> AcquireAsync(
        OutboundProxyProfileOptions profile, Uri destination, string routeKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SafeOutboundProxy.IsHttpDestination(destination) || routeKey is not { Length: > 0 and <= 80 }
            || !routeKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new OutboundProxyUnavailableException("The proxy destination or route name is invalid.");

        if (profile.Broker is not { } broker)
        {
            if (!profile.TryGetFixedEndpoint(out Uri? proxy))
                throw new OutboundProxyUnavailableException("The fixed proxy endpoint or credentials are invalid.");
            return new OutboundProxyLease(destination, proxy,
                OutboundProxyProfileOptions.GetCredentials(profile.Username, profile.Password), expiresAtUtc: null, clock);
        }

        if (profile.HasFixedSettings || !broker.TryGetEndpoints(out Uri acquire, out Uri release))
            throw new OutboundProxyUnavailableException("The proxy broker profile is invalid.");

        string keyHeader = broker.ApiKeyHeader;
        string apiKey = broker.ApiKey!;
        string[] allowedHosts = [.. broker.AllowedProxyHosts];
        using HttpClient client = clients.CreateClient(ClientName);
        client.MaxResponseContentBufferSize = MaximumLeaseBytes;
        try
        {
            string origin = new UriBuilder(destination.Scheme, destination.IdnHost.TrimEnd('.'),
                destination.IsDefaultPort ? -1 : destination.Port).Uri.GetLeftPart(UriPartial.Authority);
            using HttpRequestMessage request = Request(acquire, keyHeader, apiKey, new { origin, routeKey });
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new OutboundProxyUnavailableException("The proxy broker could not allocate a route.");

            using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement value = document.RootElement;
            string? id = Read(value, "leaseId");
            if (id is not { Length: > 0 and <= 128 } || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                || !SafeOutboundProxy.TryParseEndpoint(Read(value, "proxyUri"), out Uri? proxy)
                || !allowedHosts.Any(host => string.Equals(host.TrimEnd('.'), proxy.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                || !value.TryGetProperty("expiresAtUtc", out JsonElement expiry) || expiry.ValueKind != JsonValueKind.String
                || !expiry.TryGetDateTimeOffset(out DateTimeOffset expires) || expires <= clock.GetUtcNow())
                throw new OutboundProxyUnavailableException("The proxy broker returned an invalid or unapproved route.");

            return new OutboundProxyLease(destination, proxy,
                OutboundProxyProfileOptions.GetCredentials(Read(value, "username"), Read(value, "password")), expires, clock,
                () => ReleaseAsync(release, keyHeader, apiKey, id));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OutboundProxyUnavailableException("The proxy broker timed out.");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException
            || error is InvalidOperationException and not OutboundProxyUnavailableException)
        {
            throw new OutboundProxyUnavailableException("The proxy broker is unavailable.");
        }
    }

    private async ValueTask ReleaseAsync(Uri endpoint, string keyHeader, string apiKey, string leaseId)
    {
        using HttpClient client = clients.CreateClient(ClientName);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using HttpRequestMessage request = Request(endpoint, keyHeader, apiKey, new { leaseId });
            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            // The broker's finite expiry remains authoritative if release fails.
        }
    }

    private static HttpRequestMessage Request<TBody>(Uri endpoint, string keyHeader, string apiKey, TBody body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body) };
        request.Headers.Add(keyHeader, apiKey);
        return request;
    }

    private static string? Read(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
