using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Meta;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Meta;

/// <summary>
/// A readiness endpoint reports only the checks tagged ready, and liveness then skips them; without one, liveness keeps
/// every check.
/// </summary>
public sealed class ApiDefaultsReadinessTests
{
    [Fact]
    public async Task WithoutReadinessPath_LivenessShouldKeepEveryCheck()
    {
        await using var app = await StartAsync(readinessPath: null, databaseUp: false);

        using var health = await app.GetTestClient().GetAsync("/health");
        using var ready = await app.GetTestClient().GetAsync("/ready");

        health.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ready.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task WithReadinessPath_ShouldReportTheReadyChecks_AndLivenessShouldSkipThem(bool databaseUp, HttpStatusCode expected)
    {
        await using var app = await StartAsync("/api/system/ready", databaseUp);

        using var health = await app.GetTestClient().GetAsync("/health");
        using var ready = await app.GetTestClient().GetAsync("/api/system/ready");

        health.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task ReadinessTag_ShouldPickTheChecksTheEndpointReports()
    {
        await using var app = await StartAsync("/ready", databaseUp: false, tag: "warm");

        using var health = await app.GetTestClient().GetAsync("/health");
        using var ready = await app.GetTestClient().GetAsync("/ready");

        health.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> StartAsync(string? readinessPath, bool databaseUp, string tag = "ready")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddApiDefaults(options =>
        {
            options.EnableHttpsRedirection = false;
            options.EnableOtlpExporters = false;
            options.ExposeOpenApi = false;
            options.ReadinessEndpointPath = readinessPath;
            options.ReadinessTag = tag;
        });
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy())
            .AddCheck("database", () => databaseUp ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(), tags: ["ready"]);
        var app = builder.Build();
        app.UseApiDefaults();
        await app.StartAsync();
        return app;
    }
}
