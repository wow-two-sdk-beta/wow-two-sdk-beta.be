using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Holds one selected proxy for a destination until expiry or disposal.</summary>
public sealed class OutboundProxyLease : IAsyncDisposable
{
    private readonly Uri destination;
    private readonly NetworkCredential? credentials;
    private readonly TimeProvider clock;
    private readonly Func<ValueTask>? release;
    private int disposed;

    internal OutboundProxyLease(
        Uri destination, Uri proxyUri, NetworkCredential? credentials,
        DateTimeOffset? expiresAtUtc, TimeProvider clock, Func<ValueTask>? release = null)
    {
        this.destination = destination;
        ProxyUri = proxyUri;
        this.credentials = credentials;
        ExpiresAtUtc = expiresAtUtc;
        this.clock = clock;
        this.release = release;
    }

    /// <summary>Gets the proxy URI without its credentials.</summary>
    public Uri ProxyUri { get; }
    /// <summary>Gets the broker's expiry, or null for a fixed endpoint.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; }
    /// <summary>Gets whether the lease can start a new request.</summary>
    public bool IsActive => Volatile.Read(ref disposed) == 0
        && (ExpiresAtUtc is null || ExpiresAtUtc > clock.GetUtcNow());

    /// <summary>Creates a guarded handler that the caller disposes before releasing the lease.</summary>
    /// <param name="traffic">The request policy to apply to the destination.</param>
    /// <param name="resolveAddresses">Optional DNS resolver subject to public-address validation.</param>
    public HttpMessageHandler CreateHandler(
        OutboundProxyTraffic traffic,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddresses = null)
    {
        EnsureActive();
        return SafeOutboundProxy.CreateHandler(destination, ProxyUri, traffic, credentials, resolveAddresses, this);
    }

    /// <summary>Creates a guarded web-read client that the caller disposes before releasing the lease.</summary>
    /// <param name="resolveAddresses">Optional DNS resolver subject to public-address validation.</param>
    public HttpClient CreateWebReadClient(Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddresses = null) =>
        SafeOutboundProxy.CreateHttpClient(CreateHandler(OutboundProxyTraffic.WebRead, resolveAddresses));

    /// <summary>Releases the allocation once and rejects subsequent requests through its handlers.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && release is not null)
            await release().ConfigureAwait(false);
    }

    internal void EnsureActive()
    {
        if (!IsActive)
            throw new OutboundProxyUnavailableException("The selected proxy lease expired or was released.");
    }
}
