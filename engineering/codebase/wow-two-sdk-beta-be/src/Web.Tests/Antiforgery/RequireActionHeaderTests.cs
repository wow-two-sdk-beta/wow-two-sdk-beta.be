using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Backend.Beta.Web.Antiforgery;
using WoW.Two.Sdk.Backend.Beta.Web.ExceptionHandling;
using Xunit;

namespace WoW.Two.Sdk.Backend.Beta.Web.Tests.Antiforgery;

/// <summary>A write must name its action in the configured header; otherwise it gets an SDK validation problem.</summary>
public sealed class RequireActionHeaderTests
{
    [Fact]
    public async Task Write_NamingTheAction_Passes()
    {
        await using var app = await StartAsync(o => o.HeaderName = "X-Console-Action");

        using var request = Post("deploy", "X-Console-Action", "deploy");
        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("deployed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("rollback")]
    [InlineData("Deploy")]
    public async Task Write_WithoutTheNamedAction_GetsAnSdkValidationProblem(string? value)
    {
        await using var app = await StartAsync(o => o.HeaderName = "X-Console-Action");

        using var request = Post("deploy", "X-Console-Action", value);
        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        var error = problem.RootElement.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be(RequireActionHeaderAttribute.ErrorCode);
        error.GetProperty("property").GetString().Should().Be("X-Console-Action");
    }

    [Fact]
    public async Task Write_IsRefusedBeforeTheBodyBinds()
    {
        await using var app = await StartAsync(o => o.HeaderName = "X-Console-Action");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/action-guard/bound")
        {
            Content = JsonContent.Create(new { name = "x" }),
        };
        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ActionGuardController.BoundCalls.Should().Be(0);
    }

    [Fact]
    public async Task Guard_WithoutRegistration_ReadsTheDefaultHeader()
    {
        await using var app = await StartAsync(configure: null);

        using var request = Post("deploy", "X-Requested-Action", "deploy");
        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Post(string path, string header, string? value)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/action-guard/{path}");
        if (value is not null)
            request.Headers.Add(header, value);
        return request;
    }

    private static async Task<WebApplication> StartAsync(Action<ActionHeaderOptions>? configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAppExceptionHandling();
        if (configure is not null)
            builder.Services.AddActionHeaderGuard(configure);
        builder.Services.AddControllers().AddApplicationPart(typeof(ActionGuardController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}

[ApiController]
[Route("action-guard")]
public sealed class ActionGuardController : ControllerBase
{
    public static int BoundCalls { get; private set; }

    [HttpPost("deploy")]
    [RequireActionHeader("deploy")]
    public IActionResult Deploy() => Content("deployed");

    [HttpPost("bound")]
    [RequireActionHeader("bind")]
    public IActionResult Bound(ActionGuardBody body)
    {
        BoundCalls++;
        return Content(body.Name);
    }
}

public sealed record ActionGuardBody(string Name);
