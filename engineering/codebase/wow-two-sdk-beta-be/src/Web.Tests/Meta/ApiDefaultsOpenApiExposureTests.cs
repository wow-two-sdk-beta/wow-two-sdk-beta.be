using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Meta;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Meta;

/// <summary>The OpenAPI document is mapped by default only on a local run; deployed dev runs as Development and stays closed.</summary>
public sealed class ApiDefaultsOpenApiExposureTests
{
    private const string DocumentPath = "/openapi/v1.json";

    [Theory]
    [InlineData("Development", null, null, HttpStatusCode.OK)]
    [InlineData("Development", "dev", null, HttpStatusCode.NotFound)]
    [InlineData("Production", "prod", null, HttpStatusCode.NotFound)]
    [InlineData("Development", "dev", true, HttpStatusCode.OK)]
    [InlineData("Development", null, false, HttpStatusCode.NotFound)]
    public async Task OpenApiDocument_DefaultsToLocalRunOnly(
        string environment, string? deployEnvironment, bool? expose, HttpStatusCode expected)
    {
        await using var app = await StartAsync(environment, deployEnvironment, expose);

        using var response = await app.GetTestClient().GetAsync(DocumentPath);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task OpenApiDocument_ShouldStayAnonymous_WhenTheHostDeniesByDefault()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddApiDefaults(options =>
        {
            options.EnableHttpsRedirection = false;
            options.EnableOtlpExporters = false;
        });
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => false).Build());

        await using var app = builder.Build();
        app.UseApiDefaults(identity => identity.UseAuthorization());
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync(DocumentPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> StartAsync(string environment, string? deployEnvironment, bool? expose)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["DEPLOY_ENVIRONMENT"] = deployEnvironment;
        builder.AddApiDefaults(options =>
        {
            options.EnableHttpsRedirection = false;
            options.EnableOtlpExporters = false;
            options.ExposeOpenApi = expose;
        });

        var app = builder.Build();
        app.UseApiDefaults();
        app.MapGet("/api/ping", () => "pong");
        await app.StartAsync();
        return app;
    }
}
