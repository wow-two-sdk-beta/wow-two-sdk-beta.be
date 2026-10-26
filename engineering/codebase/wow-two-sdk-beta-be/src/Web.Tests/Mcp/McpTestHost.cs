using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WoW.Two.Sdk.Beta.Be.Ai.Mcp;
using WoW.Two.Sdk.Beta.Be.Testing.Auth;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Mcp;

/// <summary>Starts an MCP host over the test server, authenticated by a header, and speaks JSON-RPC to it.</summary>
internal static class McpTestHost
{
    public const string AuthHeader = "X-Test-Admin";

    public static async Task<WebApplication> StartAsync<TTools>(Action<McpRequestGuardOptions>? guard = null)
        where TTools : class
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddTestAuth(options => options.RequiredHeader = AuthHeader);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("mcp", policy => policy.AddAuthenticationSchemes(TestAuthHandler.SchemeName).RequireAuthenticatedUser());
        builder.Services.AddStatelessMcpServer(guard).WithTools<TTools>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuthenticatedMcp("/mcp", "mcp");
        await app.StartAsync();
        return app;
    }

    public static HttpClient Client(WebApplication app)
    {
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(AuthHeader, "1");
        return client;
    }

    public static HttpRequestMessage Request(string method, object? parameters = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = content ?? JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }),
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        return request;
    }

    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, body);
        string? json = body.Split('\n').LastOrDefault(line => line.StartsWith("data: ", StringComparison.Ordinal));
        return JsonDocument.Parse(json is null ? body : json[6..]);
    }

    public static async Task<JsonElement> CallToolAsync(HttpClient client, string tool)
    {
        using HttpResponseMessage response = await client.SendAsync(Request("tools/call", new { name = tool, arguments = new { } }));
        using JsonDocument document = await ReadAsync(response);
        return document.RootElement.Clone();
    }

    /// <summary>A tool set with one harmless read.</summary>
    [McpServerToolType]
    public sealed class EchoTools
    {
        [McpServerTool(Name = "echo", ReadOnly = true)]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public static string Echo() => "echo";
    }
}
