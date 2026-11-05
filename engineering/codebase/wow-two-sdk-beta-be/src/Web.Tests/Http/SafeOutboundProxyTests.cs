using System.Net;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

public sealed class SafeOutboundProxyTests
{
    [Theory]
    [InlineData("https://user:secret@proxy.example:443")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("socks5://[::1]:1080")]
    [InlineData("https://proxy.example:443/path")]
    public void ProxyEndpoint_ShouldRefuseCredentialsPrivateAddressesAndPaths(string value)
    {
        Assert.False(SafeOutboundProxy.TryParseEndpoint(value, out _));
    }

    [Theory]
    [InlineData("http://source.example/")]
    [InlineData("https://source.example:444/")]
    [InlineData("https://other.example/")]
    [InlineData("https://user:secret@source.example/")]
    public async Task ProxyRequest_ShouldRefuseDifferentOriginBeforeResolving(string target)
    {
        using HttpClient client = Create((_, _) => throw new InvalidOperationException("Unexpected DNS resolution."));
        await Assert.ThrowsAsync<OutboundAddressBlockedException>(() => client.GetAsync(target));
    }

    [Fact]
    public async Task ProxyRequest_ShouldRefuseHostOverridesBeforeResolving()
    {
        using HttpClient client = Create((_, _) => throw new InvalidOperationException("Unexpected DNS resolution."));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://source.example/");
        request.Headers.Host = "other.example";
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request));
    }

    [Fact]
    public async Task ProxyRequest_ShouldRefuseMixedSourceDnsBeforeResolvingProxy()
    {
        using HttpClient client = Create((host, _) =>
        {
            Assert.Equal("source.example", host);
            return Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Loopback });
        });
        await Assert.ThrowsAsync<OutboundAddressBlockedException>(() => client.GetAsync("https://source.example/"));
    }

    [Fact]
    public async Task ProxyRequest_ShouldRefusePrivateProxyDnsBeforeConnecting()
    {
        var resolved = new List<string>();
        using HttpClient client = Create((host, _) =>
        {
            resolved.Add(host);
            return Task.FromResult(new[] { host == "source.example" ? IPAddress.Parse("1.1.1.1") : IPAddress.Loopback });
        });
        HttpRequestException error = await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => client.GetAsync("https://source.example/"));
        Assert.IsType<OutboundAddressBlockedException>(error.InnerException);
        Assert.Contains("proxy.example", resolved);
    }

    private static HttpClient Create(Func<string, CancellationToken, Task<IPAddress[]>> resolve) =>
        SafeOutboundProxy.CreateClient(new Uri("https://source.example/"), new Uri("http://proxy.example:3128"), resolveAddresses: resolve);
}
