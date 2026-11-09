# Outbound HTTP safety

*Last updated: 2026-09-26*

## Contract

`AddSafeOutboundHttp` is opt-in on a named or typed `IHttpClientBuilder`. It validates
absolute HTTP(S) destinations, requires HTTPS by default, rejects URI credentials and
optionally restricts exact normalized DNS hosts. Address validation occurs at connection
time and the socket dials only the validated addresses. Redirects, system proxies and
shared cookies are disabled. HTTP/3 and version-upgrade requests are rejected.

The caller must consume redirect responses explicitly, reapply its business policy, and
send any approved destination through the same guarded client. The extension owns the primary
handler; replacing or reconfiguring that handler can invalidate its guarantees.
Private network access requires explicit registration configuration.
This transport does not enforce response-size limits, business URL allowlists or request replay policy.

## Usage

```csharp
services.AddHttpClient("remote-document")
    .AddSafeOutboundHttp(options => options.AllowedHosts.Add("example.com"))
    .AddSdkResilience();
```

## Explicit proxy

`SafeOutboundProxy.CreateClient` creates a caller-owned client for one source
origin and one configured HTTP, HTTPS or SOCKS endpoint. It accepts GET and
HEAD and returns redirects to the caller. It verifies that the entire source DNS
set is public before sending each request. It verifies the proxy's DNS set and
dials only the validated addresses. The proxy needs its own public-target policy
because it resolves the destination remotely.

Supply proxy credentials through `NetworkCredential` from the caller's secret
provider. The client rejects URI credentials and explicit Host or proxy
authorization headers. It uses the caller's cancellation and deadline. Source
selection, lease acquisition, usage limits and retries belong to the caller.

```csharp
using var client = SafeOutboundProxy.CreateClient(source, proxyEndpoint, credentials);
using var response = await client.GetAsync(source, cancellationToken);
```

### Proxy profiles

Bind named `OutboundProxyProfileOptions` from application settings. Each profile
holds a fixed `Server` with optional `Username` and `Password`, or a `Broker`
with an HTTPS base URL, API key, acquire and release paths, and allowed proxy
hosts. Inject secrets from the application's secret store. A standard HTTP,
HTTPS or SOCKS endpoint uses the same profile shape across providers.

Register `AddOutboundProxyBroker()`. Pass the selected profile, destination and
route name to `AcquireAsync`. A broker profile posts `origin` and `routeKey` to
acquire, validates the returned `leaseId`, `proxyUri` and `expiresAtUtc`, and
posts the lease ID to release. Acquisition makes one request. A fixed profile
resolves locally. The lease owns the allocation and blocks new requests after
expiry or disposal. Close its clients and channels before disposing the lease.

### Outbound request policies

Use `SafeOutboundProxy.CreateHandler` for a fixed endpoint or
`lease.CreateHandler` for an acquired endpoint. Select the policy at the
destination boundary. An HTTP proxy must support CONNECT for HTTPS destinations.

- `WebRead` allows GET and HEAD on one HTTP(S) origin.
- `GraphQl` allows GET and POST to one exact HTTPS endpoint path.
- `Grpc` allows native gRPC POSTs to one HTTPS service path such as
  `/example.Catalog/`. Configure `GrpcChannelOptions.HttpHandler` with this
  handler and require HTTP/2 with `RequestVersionExact`. The handler passes
  response streams and trailers through to the channel.

All three policies guard the destination origin and proxy address, preserve
TLS validation and return redirects without following them. Apply service
authorization and deadlines in the caller. Switch a route's profile in config
and reacquire the lease before constructing a new client or channel.

```csharp
var service = new Uri("https://api.example/example.Catalog/");
await using var lease = await broker.AcquireAsync(profiles[profileName], service, "catalog", cancellationToken);
using var handler = lease.CreateHandler(OutboundProxyTraffic.Grpc);
using var channel = GrpcChannel.ForAddress(service.GetLeftPart(UriPartial.Authority), new GrpcChannelOptions
{
    HttpHandler = handler,
    HttpVersion = HttpVersion.Version20,
    HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
});
// Invoke the generated gRPC client while the channel and lease are alive.
```
