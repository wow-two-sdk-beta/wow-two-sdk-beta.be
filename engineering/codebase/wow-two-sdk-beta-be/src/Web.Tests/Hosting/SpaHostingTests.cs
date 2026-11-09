using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using WoW.Two.Sdk.Beta.Be.Web.Hosting;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Hosting;

/// <summary>A single-host SPA: route folders answer their own address, other routes fall back to the shell.</summary>
public sealed class SpaHostingTests : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("spa-hosting-").FullName;

    public SpaHostingTests()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "shell");
        Directory.CreateDirectory(Path.Combine(_webRoot, "pricing"));
        File.WriteAllText(Path.Combine(_webRoot, "pricing", "index.html"), "pricing");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app-3f9a.js"), "console.log('app');");
        File.WriteAllText(Path.Combine(_webRoot, "robots.txt"), "User-agent: *");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "garment.glb"), "glTF");
        Directory.CreateDirectory(Path.Combine(_webRoot, "studio", "drafts"));
        Directory.CreateDirectory(Path.Combine(_webRoot, "studio", "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "studio", "index.html"), "studio");
        File.WriteAllText(Path.Combine(_webRoot, "studio", "drafts", "index.html"), "drafts");
        File.WriteAllText(Path.Combine(_webRoot, "studio", "assets", "editor-7c1d.js"), "console.log('editor');");
    }

    [Theory]
    [InlineData("/pricing", "pricing", false)]
    [InlineData("/pricing/", "pricing", false)]
    [InlineData("/", "shell", false)]
    [InlineData("/app/codes", "shell", false)]
    [InlineData("/pricing", "pricing", true)]
    [InlineData("/pricing/", "pricing", true)]
    [InlineData("/", "shell", true)]
    [InlineData("/app/codes", "shell", true)]
    public async Task Route_ShouldServeItsOwnDocument_OrTheShell(string path, string expected, bool routingFirst)
    {
        await using var app = await StartAsync(_ => { }, routingFirst);

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData("/assets/app-3f9a.js", "public, max-age=31536000, immutable")]
    [InlineData("/", "no-cache")]
    [InlineData("/pricing", "no-cache")]
    [InlineData("/app/codes", "no-cache")]
    [InlineData("/robots.txt", null)]
    public async Task Response_ShouldCacheHashedAssets_AndRevalidateDocuments(string path, string? cacheControl)
    {
        await using var app = await StartAsync(_ => { }, routingFirst: false);

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().Should().Be(cacheControl);
    }

    [Fact]
    public async Task RouteFolder_ShouldRedirectToTheSlashedPath_WhenAsked()
    {
        await using var app = await StartAsync(options => options.RedirectToTrailingSlash = true, routingFirst: false);

        using var response = await app.GetTestClient().GetAsync("/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().Should().EndWith("/pricing/");
    }

    [Fact]
    public async Task UnknownExtension_ShouldBeServed_WhenItsContentTypeIsMapped()
    {
        await using var app = await StartAsync(spa => spa.ContentTypes[".glb"] = "model/gltf-binary", routingFirst: false);

        using var response = await app.GetTestClient().GetAsync("/assets/garment.glb");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("model/gltf-binary");
        response.Headers.CacheControl?.ToString().Should().Be("public, max-age=31536000, immutable");
    }

    [Fact]
    public async Task UnknownExtension_ShouldNotBeServed_WhenItsContentTypeIsNotMapped()
    {
        await using var app = await StartAsync(_ => { }, routingFirst: false);

        using var response = await app.GetTestClient().GetAsync("/assets/garment.glb");

        (await response.Content.ReadAsStringAsync()).Should().NotBe("glTF");
    }

    [Theory]
    [InlineData("/pricing/../../secret")]
    [InlineData("/%2e%2e/secret")]
    public async Task ClimbingPath_ShouldGetTheShell(string path)
    {
        await using var app = await StartAsync(_ => { });

        using var response = await app.GetTestClient().GetAsync(path);

        (await response.Content.ReadAsStringAsync()).Should().Be("shell");
    }

    [Fact]
    public async Task UnmatchedApiRoute_ShouldAnswerJson404_NotTheShell()
    {
        await using var app = await StartAsync(_ => { });

        using var response = await app.GetTestClient().GetAsync("/api/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/openid-configuration/mcp")]
    [InlineData("/internal/missing")]
    public async Task UnmatchedReservedRoute_ShouldAnswerJson404_NotTheShell(string path)
    {
        await using var app = await StartAsync(spa => spa.ReservedPathPrefixes.Add("/internal/"));

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ClearedReservedPrefixes_ShouldServeTheShell()
    {
        await using var app = await StartAsync(spa => spa.ReservedPathPrefixes.Clear());

        using var response = await app.GetTestClient().GetAsync("/.well-known/oauth-protected-resource");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("shell");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task RootPrefix_ShouldServeTheRootShell(string prefix)
    {
        new SpaHostingOptions().PathPrefix.Should().BeEmpty();
        await using var app = await StartAsync(spa => spa.PathPrefix = prefix);

        using var response = await app.GetTestClient().GetAsync("/app/codes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("shell");
    }

    [Theory]
    [InlineData("/studio/", "studio", true)]
    [InlineData("/studio/posts/42", "studio", true)]
    [InlineData("/studio/drafts", "drafts", true)]
    [InlineData("/studio/drafts/", "drafts", true)]
    [InlineData("/studio/", "studio", false)]
    [InlineData("/studio/posts/42", "studio", false)]
    [InlineData("/studio/drafts", "drafts", false)]
    [InlineData("/studio/drafts/", "drafts", false)]
    public async Task PrefixedRoute_ShouldServeItsOwnDocument_OrThePrefixedShell(string path, string expected, bool routingFirst)
    {
        await using var app = await StartAsync(spa => spa.PathPrefix = "/studio", routingFirst);

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/posts/missing", true)]
    [InlineData("/pricing", true)]
    [InlineData("/api/missing", true)]
    [InlineData("/.well-known/oauth-protected-resource", true)]
    [InlineData("/", false)]
    [InlineData("/posts/missing", false)]
    [InlineData("/pricing", false)]
    [InlineData("/api/missing", false)]
    [InlineData("/.well-known/oauth-protected-resource", false)]
    public async Task PathOutsideThePrefix_ShouldAnswerJson404_NotAShell(string path, bool routingFirst)
    {
        await using var app = await StartAsync(spa => spa.PathPrefix = "/studio", routingFirst);

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("/studio", "/studio/", true)]
    [InlineData("/studio?draft=7", "/studio/?draft=7", true)]
    [InlineData("/studio", "/studio/", false)]
    [InlineData("/studio?draft=7", "/studio/?draft=7", false)]
    public async Task BarePrefix_ShouldRedirectToTheSlashedPath(string path, string location, bool routingFirst)
    {
        await using var app = await StartAsync(spa => spa.PathPrefix = "/studio", routingFirst);

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be(location);
    }

    [Theory]
    [InlineData("/studio/assets/editor-7c1d.js", "public, max-age=31536000, immutable")]
    [InlineData("/studio/", "no-cache")]
    [InlineData("/studio/drafts", "no-cache")]
    [InlineData("/studio/posts/42", "no-cache")]
    [InlineData("/robots.txt", null)]
    public async Task PrefixedResponse_ShouldCacheHashedAssets_AndRevalidateDocuments(string path, string? cacheControl)
    {
        await using var app = await StartAsync(spa => spa.PathPrefix = "/studio", routingFirst: false);

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().Should().Be(cacheControl);
    }

    [Theory]
    [InlineData("studio")]
    [InlineData("studio/")]
    [InlineData("//studio//")]
    public async Task Prefix_ShouldBeNormalized(string prefix)
    {
        await using var app = await StartAsync(spa => spa.PathPrefix = prefix);

        using var response = await app.GetTestClient().GetAsync("/studio/posts/42");

        (await response.Content.ReadAsStringAsync()).Should().Be("studio");
    }

    [Theory]
    [InlineData("/studio/../admin")]
    [InlineData("/studio?draft=7")]
    [InlineData("/studio#top")]
    public async Task Prefix_ShouldBeRejected_WhenItIsNotAPlainPath(string prefix)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = _webRoot });
        await using var app = builder.Build();

        var useHosting = () => app.UseSpaHosting(spa => spa.PathPrefix = prefix);
        var mapFallback = () => app.MapSpaFallback(spa => spa.PathPrefix = prefix);

        useHosting.Should().Throw<ArgumentException>();
        mapFallback.Should().Throw<ArgumentException>();
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    // Routing first is the pipeline WebApplication builds itself; after is an app calling UseRouting behind the SPA.
    private async Task<WebApplication> StartAsync(Action<SpaHostingOptions> configure, bool routingFirst = true)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = _webRoot });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseSpaHosting(configure);
        if (!routingFirst)
            app.UseRouting();
        app.MapSpaFallback(configure);
        await app.StartAsync();
        return app;
    }
}
