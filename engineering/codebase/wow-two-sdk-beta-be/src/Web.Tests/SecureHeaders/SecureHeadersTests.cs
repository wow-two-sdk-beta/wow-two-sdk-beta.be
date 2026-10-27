using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Web.SecureHeaders;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.SecureHeaders;

/// <summary>
/// Covers the cross-origin opt-outs on <c>UseOwaspSecureHeaders</c> over an in-memory TestServer host: the
/// unconfigured call emits the opener and embedder policies, and either flag switched off drops that header
/// while the rest of the preset stays.
/// </summary>
public sealed class SecureHeadersTests
{
    private const string OpenerPolicyHeader = "Cross-Origin-Opener-Policy";

    private const string EmbedderPolicyHeader = "Cross-Origin-Embedder-Policy";

    private const string ResourcePolicyHeader = "Cross-Origin-Resource-Policy";

    private const string Sandbox = "default-src 'none'; sandbox";

    /// <summary>Boots an in-memory TestServer host whose only middleware is the preset under <paramref name="configure"/>.</summary>
    private static async Task<WebApplication> StartHeaderHostAsync(Action<SecureHeadersOptions>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var app = builder.Build();

        app.UseOwaspSecureHeaders(configure);
        app.MapGet("/", () => "ok");
        app.MapGet("/media", () => "bytes").AllowCrossOriginEmbedding();
        app.MapGet("/poster", [AllowCrossOriginEmbedding] () => "image");
        app.MapGet("/user-content", () => "<p>upload</p>")
            .OverrideSecureHeader("Content-Security-Policy", Sandbox)
            .OverrideSecureHeader(EmbedderPolicyHeader, null);
        app.MapGet("/legacy", [SecureHeaderOverride("X-Frame-Options", "SAMEORIGIN")] () => "framed");

        await app.StartAsync();

        return app;
    }

    private static async Task<HttpResponseMessage> GetRootAsync(WebApplication app, string path = "/")
    {
        using var client = app.GetTestClient();

        return await client.GetAsync(new Uri(path, UriKind.Relative));
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldEmitBothCrossOriginPolicies_WhenUnconfigured()
    {
        await using var app = await StartHeaderHostAsync();

        using var response = await GetRootAsync(app);

        response.Headers.GetValues(OpenerPolicyHeader).Should().ContainSingle().Which.Should().Be("same-origin");
        response.Headers.GetValues(EmbedderPolicyHeader).Should().ContainSingle().Which.Should().Be("require-corp");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldDropOpenerPolicyOnly_WhenOpenerOptedOut()
    {
        await using var app = await StartHeaderHostAsync(options => options.EnableCrossOriginOpenerPolicy = false);

        using var response = await GetRootAsync(app);

        response.Headers.Contains(OpenerPolicyHeader).Should().BeFalse();
        response.Headers.GetValues(EmbedderPolicyHeader).Should().ContainSingle().Which.Should().Be("require-corp");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldAllowOpenerPopups_WhenPopupsAreAllowed()
    {
        await using var app = await StartHeaderHostAsync(options => options.AllowOpenerPopups = true);

        using var response = await GetRootAsync(app);

        response.Headers.GetValues(OpenerPolicyHeader).Should().ContainSingle().Which.Should().Be("same-origin-allow-popups");
        response.Headers.GetValues(EmbedderPolicyHeader).Should().ContainSingle().Which.Should().Be("require-corp");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldDropEmbedderPolicyOnly_WhenEmbedderOptedOut()
    {
        await using var app = await StartHeaderHostAsync(options => options.EnableCrossOriginEmbedderPolicy = false);

        using var response = await GetRootAsync(app);

        response.Headers.Contains(EmbedderPolicyHeader).Should().BeFalse();
        response.Headers.GetValues(OpenerPolicyHeader).Should().ContainSingle().Which.Should().Be("same-origin");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldKeepTheRestOfThePreset_WhenBothOptedOut()
    {
        await using var app = await StartHeaderHostAsync(options =>
        {
            options.EnableCrossOriginOpenerPolicy = false;
            options.EnableCrossOriginEmbedderPolicy = false;
        });

        using var response = await GetRootAsync(app);

        response.Headers.Contains(OpenerPolicyHeader).Should().BeFalse();
        response.Headers.Contains(EmbedderPolicyHeader).Should().BeFalse();
        response.Headers.GetValues(ResourcePolicyHeader).Should().ContainSingle().Which.Should().Be("same-origin");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldKeepTheSameOriginResourcePolicy_WhenTheEndpointIsUnmarked()
    {
        await using var app = await StartHeaderHostAsync();

        using var response = await GetRootAsync(app);

        response.Headers.GetValues(ResourcePolicyHeader).Should().ContainSingle().Which.Should().Be("same-origin");
    }

    [Theory]
    [InlineData("/media")]
    [InlineData("/poster")]
    public async Task UseOwaspSecureHeaders_ShouldAllowCrossOriginEmbedding_WhenTheEndpointIsMarked(string path)
    {
        await using var app = await StartHeaderHostAsync();

        using var response = await GetRootAsync(app, path);

        response.Headers.GetValues(ResourcePolicyHeader).Should().ContainSingle().Which.Should().Be("cross-origin");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldAnswerTheEndpointsOwnHeaders_WhenTheEndpointOverridesThem()
    {
        await using var app = await StartHeaderHostAsync();

        using var response = await GetRootAsync(app, "/user-content");

        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Which.Should().Be(Sandbox);
        response.Headers.Contains(EmbedderPolicyHeader).Should().BeFalse();
        response.Headers.GetValues(OpenerPolicyHeader).Should().ContainSingle().Which.Should().Be("same-origin");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public async Task UseOwaspSecureHeaders_ShouldAnswerTheAttributesHeader_WhenTheEndpointCarriesTheAttribute()
    {
        await using var app = await StartHeaderHostAsync();

        using var response = await GetRootAsync(app, "/legacy");

        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("SAMEORIGIN");
    }
}
