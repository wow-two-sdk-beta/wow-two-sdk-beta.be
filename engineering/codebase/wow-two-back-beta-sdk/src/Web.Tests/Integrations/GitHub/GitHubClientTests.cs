using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Backend.Beta.Integrations;
using WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;
using Xunit;

namespace WoW.Two.Sdk.Backend.Beta.Web.Tests.Integrations.GitHub;

/// <summary>Drives the GitHub client against a recording handler: request paths and status mapping.</summary>
public sealed class GitHubClientTests
{
    [Fact]
    public async Task FileExistsAsync_ShouldEscapeEachPathSegmentButKeepSeparators()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Build(handler);

        var result = await client.FileExistsAsync("owner/repo", "docs/a b/#notes?.md", CancellationToken.None);

        result.Should().Be(FileCheck.Present);
        handler.Paths.Single().Should().Be("/repos/owner/repo/contents/docs/a%20b/%23notes%3F.md");
    }

    [Fact]
    public async Task GetLatestWorkflowRunAsync_ShouldEscapeTheWorkflowFileName()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Build(handler);

        var result = await client.GetLatestWorkflowRunAsync("owner/repo", "build & ship.yml", CancellationToken.None);

        result.Should().Be(BuildRunCheck.None);
        handler.Paths.Single().Should().Be("/repos/owner/repo/actions/workflows/build%20%26%20ship.yml/runs");
    }

    private static IGitHubClient Build(RecordingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessTokenService>(new StaticToken("token"));
        services.AddGitHubIntegration().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IGitHubClient>();
    }

    private sealed class StaticToken(string? token) : IAccessTokenService
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(token);
    }

    /// <summary>Answers each request from a script and records the escaped request path.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }
}
