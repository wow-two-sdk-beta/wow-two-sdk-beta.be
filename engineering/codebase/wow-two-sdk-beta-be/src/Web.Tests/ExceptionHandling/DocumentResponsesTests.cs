using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.ExceptionHandling;

/// <summary>A public site's document routes answer their own error pages; every other route keeps problem JSON.</summary>
public sealed class DocumentResponsesTests
{
    private const string NotFoundPage = "<!doctype html><title>Not found</title><p>This page is gone.</p>";

    [Theory]
    [InlineData("/site/missing-page")]
    [InlineData("/site-pages/missing")]
    [InlineData("/document/missing")]
    [InlineData("/journal/missing")]
    public async Task MarkedEndpoint_ShouldKeepItsEmpty404(string path, string method = "GET")
    {
        await using var app = await Host();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/site/page")]
    [InlineData("/site-pages/page")]
    public async Task MarkedControllerAction_ShouldKeepItsHtml404(string path)
    {
        await using var app = await Host();

        using var response = await app.GetTestClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundPage, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/site/api-page", 404)]
    [InlineData("/site/api-missing", 404)]
    [InlineData("/api/missing", 404)]
    [InlineData("/no-route", 404)]
    [InlineData("/document/throws", 500)]
    public async Task UnmarkedSiblingOrException_ShouldAnswerProblemJson(string path, int status)
    {
        await using var app = await Host();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("This page is gone", text);
        Assert.DoesNotContain("private-", text);
    }

    private static async Task<WebApplication> Host()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpProblemDetails();
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentSiteController).Assembly);
        var app = builder.Build();
        app.UseHttpProblemDetails();
        app.MapControllers();
        app.MapGet("/document/missing", () => Results.NotFound()).WithDocumentResponses();
        app.MapGet("/document/throws", void () => throw new InvalidOperationException("private-secret")).WithDocumentResponses();
        app.MapGet("/api/missing", () => Results.NotFound());
        app.MapGroup("/journal").WithDocumentResponses().MapGet("/{slug}", () => Results.NotFound());
        await app.StartAsync();
        return app;
    }

    internal static ContentResult Page(ControllerBase controller)
    {
        var page = controller.Content(NotFoundPage, "text/html; charset=utf-8");
        page.StatusCode = StatusCodes.Status404NotFound;
        return page;
    }
}

[Route("site")]
public sealed class DocumentSiteController : ControllerBase
{
    [DocumentResponses]
    [HttpGet("page")]
    public IActionResult MarkedPage() => DocumentResponsesTests.Page(this);

    [DocumentResponses]
    [HttpGet("missing-page")]
    public IActionResult MarkedMissing() => NotFound();

    [HttpGet("api-page")]
    public IActionResult UnmarkedPage() => DocumentResponsesTests.Page(this);

    [HttpGet("api-missing")]
    public IActionResult UnmarkedMissing() => NotFound();
}

[DocumentResponses]
[Route("site-pages")]
public sealed class DocumentPagesController : ControllerBase
{
    [HttpGet("page")]
    public IActionResult Page() => DocumentResponsesTests.Page(this);

    [HttpGet("missing")]
    public IActionResult Missing() => NotFound();
}
