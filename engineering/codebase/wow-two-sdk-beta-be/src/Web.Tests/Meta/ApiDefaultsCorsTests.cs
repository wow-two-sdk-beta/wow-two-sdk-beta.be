using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Meta;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Meta;

/// <summary>An <c>AddApiDefaults</c> host with <c>CorsOrigins</c> answers those origins — preflight and request alike — and no others.</summary>
public sealed class ApiDefaultsCorsTests
{
    private const string AllowedOrigin = "capacitor://localhost";

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddApiDefaults(options =>
        {
            options.EnableHttpsRedirection = false;
            options.EnableOtlpExporters = false;
            options.ExposeOpenApi = false;
            options.CorsOrigins.Add(AllowedOrigin);
        });
        var app = builder.Build();
        app.UseApiDefaults();
        app.MapPut("/items/{id}", (string id) => Results.Ok(id));
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Preflight_ShouldAllowTheConfiguredOrigin()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/items/1");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await app.GetTestServer().CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(AllowedOrigin);
    }

    [Theory]
    [InlineData(AllowedOrigin, true)]
    [InlineData("https://elsewhere.example", false)]
    public async Task Request_ShouldCarryTheAllowHeader_OnlyForTheConfiguredOrigin(string origin, bool allowed)
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/items/1");
        request.Headers.Add("Origin", origin);

        using var response = await app.GetTestServer().CreateClient().SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
    }
}
