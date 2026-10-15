using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;
using WoW.Two.Sdk.Backend.Beta.Web.ExceptionHandling;
using WoW.Two.Sdk.Backend.Beta.Web.ExceptionHandling.Extensions;
using Xunit;

namespace WoW.Two.Sdk.Backend.Beta.Web.Tests.ExceptionHandling;

/// <summary>An application result renders as the success envelope or as SDK problem details.</summary>
public sealed class ControllerResultExtensionsTests
{
    [Fact]
    public async Task Success_ShouldRenderTheEnvelopeWith200()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/result-render/ok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("data").GetProperty("name").GetString().Should().Be("alpha");
    }

    [Fact]
    public async Task Success_WithACustomResponse_ShouldKeepTheEnvelope()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/result-render/created");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be("/items/alpha");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("data").GetProperty("name").GetString().Should().Be("alpha");
    }

    [Fact]
    public async Task Failure_ShouldRenderSdkProblemDetails()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/result-render/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be(nameof(AppErrorType.NotFound));
        body.RootElement.GetProperty("detail").GetString().Should().Be("No such item.");
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAppExceptionHandling();
        builder.Services.AddControllers().AddApplicationPart(typeof(ResultRenderController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}

[ApiController]
[Route("result-render")]
public sealed class ResultRenderController : ControllerBase
{
    [HttpGet("ok")]
    public IActionResult Found() => this.ToActionResult(AppResult<ResultRenderItem>.Ok(new ResultRenderItem("alpha")));

    [HttpGet("created")]
    public IActionResult Create() => this.ToActionResult(
        AppResult<ResultRenderItem>.Ok(new ResultRenderItem("alpha")),
        envelope => Created($"/items/{envelope.Data.Name}", envelope));

    [HttpGet("missing")]
    public IActionResult Missing() => this.ToActionResult(
        AppResult<ResultRenderItem>.Fail(AppErrorFactory.NotFound("No such item.")));
}

public sealed record ResultRenderItem(string Name);
