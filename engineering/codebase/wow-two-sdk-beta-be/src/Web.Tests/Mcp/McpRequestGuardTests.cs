using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Mcp;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Mcp;

/// <summary>Every endpoint <c>MapAuthenticatedMcp</c> maps refuses a foreign browser origin with 403 and an oversized body with 413.</summary>
public sealed class McpRequestGuardTests
{
    [Fact]
    public async Task ForeignOrigin_IsForbidden()
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>();
        using HttpClient client = McpTestHost.Client(app);
        using HttpRequestMessage request = McpTestHost.Request("tools/list");
        request.Headers.Add("Origin", "https://rebound.example");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("origin");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:6274")]
    public async Task NoOriginOwnOriginAndConfiguredOrigins_Pass(string? origin)
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>(
            guard => guard.AllowedOrigins = ["http://localhost:6274"]);
        using HttpClient client = McpTestHost.Client(app);
        using HttpRequestMessage request = McpTestHost.Request("tools/list");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        using var payload = await McpTestHost.ReadAsync(response);

        payload.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("name").GetString().Should().Be("echo");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("http://localhost:6275")]
    [InlineData("https://localhost")]
    public async Task OpaqueOrNearMissOrigins_AreForbidden(string origin)
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>(
            guard => guard.AllowedOrigins = ["http://localhost:6274"]);
        using HttpClient client = McpTestHost.Client(app);
        using HttpRequestMessage request = McpTestHost.Request("tools/list");
        request.Headers.Add("Origin", origin);

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnauthenticatedForeignOrigin_IsRefusedBeforeTheTransport()
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>();
        using HttpClient client = app.GetTestClient();
        using HttpRequestMessage request = McpTestHost.Request("tools/list");
        request.Headers.Add("Origin", "https://rebound.example");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeclaredOversizedBody_IsRefused()
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>(guard => guard.MaxRequestBodyBytes = 1024);
        using HttpClient client = McpTestHost.Client(app);
        using HttpRequestMessage request = McpTestHost.Request("tools/list", new { padding = new string('x', 4096) });

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task ChunkedOversizedBody_IsRefusedToo()
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>(guard => guard.MaxRequestBodyBytes = 1024);
        using HttpClient client = McpTestHost.Client(app);
        string json = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{"padding":""" + "\"" + new string('x', 4096) + "\"}}";
        var content = new UnsizedContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using HttpRequestMessage request = McpTestHost.Request("tools/list", content: content);

        using HttpResponseMessage response = await client.SendAsync(request);

        request.Content!.Headers.ContentLength.Should().BeNull("the body streams without a declared length");
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task BodyWithinTheLimit_ReachesTheTransportWhole()
    {
        await using var app = await McpTestHost.StartAsync<McpTestHost.EchoTools>(guard => guard.MaxRequestBodyBytes = null);
        using HttpClient client = McpTestHost.Client(app);
        using HttpRequestMessage request = McpTestHost.Request(
            "tools/call", new { name = "echo", arguments = new { }, padding = new string('x', 128 * 1024) });

        using HttpResponseMessage response = await client.SendAsync(request);
        using var payload = await McpTestHost.ReadAsync(response);

        payload.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().Should().Be("echo");
    }

    [Fact]
    public async Task MalformedAllowedOrigin_FailsTheBoot()
    {
        var act = () => McpTestHost.StartAsync<McpTestHost.EchoTools>(guard => guard.AllowedOrigins = ["localhost:6274"]);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Theory]
    [InlineData("https://wheelhouse.tail1234.ts.net", "https://wheelhouse.tail1234.ts.net")]
    [InlineData("HTTPS://Wheelhouse.Example:443/", "https://wheelhouse.example")]
    [InlineData("https://wheelhouse.example:8443", "https://wheelhouse.example:8443")]
    [InlineData("http://localhost:6274", "http://localhost:6274")]
    public void NormalizeOrigin_KeepsOnlySchemeHostAndPort(string value, string expected) =>
        McpOriginExtensions.NormalizeOrigin(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("wheelhouse.example")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://wheelhouse.example")]
    [InlineData("https://wheelhouse.example/mcp")]
    [InlineData("https://wheelhouse.example?next=1")]
    [InlineData("https://owner@wheelhouse.example")]
    public void NormalizeOrigin_RefusesAnythingButAnHttpOrigin(string? value) =>
        McpOriginExtensions.NormalizeOrigin(value).Should().BeNull();

    /// <summary>A body that declares no length, so it streams chunked.</summary>
    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
