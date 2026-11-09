using System.Net;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

public sealed class OutboundProxyBrokerTests
{
    private static readonly Uri Source = new("https://source.example/graphql");

    [Theory]
    [InlineData("http://proxy.example:8080")]
    [InlineData("https://proxy.example:443")]
    [InlineData("socks4://proxy.example:1080")]
    [InlineData("socks4a://proxy.example:1080")]
    [InlineData("socks5://proxy.example:1080")]
    public async Task AcquireUsesTheConfiguredEndpointWithoutContactingABroker(string endpoint)
    {
        var http = new BrokerHttp();
        var broker = new OutboundProxyBroker(http, TimeProvider.System);
        await using OutboundProxyLease lease = await broker.AcquireAsync(
            new OutboundProxyProfileOptions { Server = endpoint, Username = "proxy-user", Password = "proxy-secret" },
            Source, "catalog");
        Assert.Equal(new Uri(endpoint), lease.ProxyUri);
        Assert.Null(lease.ExpiresAtUtc);
        Assert.Empty(http.Paths);
    }

    [Fact]
    public async Task AcquireFencesExpiredRequestsAndReleasesExactlyOnce()
    {
        var http = new BrokerHttp();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));
        var broker = new OutboundProxyBroker(http, clock);
        var profile = new OutboundProxyProfileOptions
        {
            Broker = new OutboundProxyBrokerOptions
            {
                BaseUrl = "https://broker.example/v1/",
                ApiKey = "broker-secret",
                AllowedProxyHosts = ["proxy.example"],
            },
        };
        await using OutboundProxyLease lease = await broker.AcquireAsync(profile, Source, "catalog");
        Assert.True(lease.IsActive);
        using var client = new HttpClient(lease.CreateHandler(OutboundProxyTraffic.GraphQl,
            (_, _) => throw new InvalidOperationException("Unexpected DNS resolution.")));
        clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<OutboundProxyUnavailableException>(() => client.GetAsync(Source));
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(["/v1/leases/acquire", "/v1/leases/release"], http.Paths);
    }

    [Fact]
    public async Task AcquireRejectsAmbiguousProfilesWithoutContactingABroker()
    {
        var http = new BrokerHttp();
        var broker = new OutboundProxyBroker(http, TimeProvider.System);
        var profile = new OutboundProxyProfileOptions
        {
            Server = "https://proxy.example",
            Broker = new OutboundProxyBrokerOptions
            {
                BaseUrl = "https://broker.example/v1/",
                ApiKey = "broker-secret",
                AllowedProxyHosts = ["proxy.example"],
            },
        };
        Assert.False(profile.IsValid());
        await Assert.ThrowsAsync<OutboundProxyUnavailableException>(
            () => broker.AcquireAsync(profile, Source, "catalog").AsTask());
        Assert.Empty(http.Paths);
    }

    private sealed class BrokerHttp : IHttpClientFactory
    {
        internal List<string> Paths { get; } = [];

        public HttpClient CreateClient(string name) => new(new Handler(Paths));

        private sealed class Handler(List<string> paths) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.Equal("broker.example", request.RequestUri!.Host);
                Assert.Equal("broker-secret", Assert.Single(request.Headers.GetValues("X-Api-Key")));
                paths.Add(request.RequestUri.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"leaseId":"lease-1","proxyUri":"https://proxy.example:443","expiresAtUtc":"2026-11-09T00:01:00Z"}"""),
                });
            }
        }
    }
}
