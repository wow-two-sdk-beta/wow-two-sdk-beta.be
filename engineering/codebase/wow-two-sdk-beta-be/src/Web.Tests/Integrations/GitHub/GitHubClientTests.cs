using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Integrations.GitHub;

/// <summary>Drives the GitHub client against a recording handler: request paths, headers and status mapping.</summary>
public sealed class GitHubClientTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task FileExistsAsync_ShouldEscapeEachPathSegmentButKeepSeparators()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Build(handler);

        var result = await client.FileExistsAsync("owner/repo", "docs/a b/#notes?.md", CancellationToken.None);

        result.Should().Be(FileCheck.Present);
        handler.Requests.Single().Path.Should().Be("/repos/owner/repo/contents/docs/a%20b/%23notes%3F.md");
    }

    [Fact]
    public async Task GetLatestWorkflowRunAsync_ShouldEscapeTheWorkflowFileName()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Build(handler);

        var result = await client.GetLatestWorkflowRunAsync("owner/repo", "build & ship.yml", CancellationToken.None);

        result.Should().Be(BuildRunCheck.None);
        handler.Requests.Single().Path.Should().Be("/repos/owner/repo/actions/workflows/build%20%26%20ship.yml/runs");
    }

    [Fact]
    public async Task GetBranchHeadAsync_ShouldReadTheBareShaOfTheEscapedBranch()
    {
        var handler = new RecordingHandler(_ => Text(HttpStatusCode.OK, Sha + "\n"));
        var client = Build(handler);

        var head = await client.GetBranchHeadAsync("owner/repo", "feature/x", CancellationToken.None);

        head.Should().Be(new BranchHead { Outcome = RepositoryLookup.Found, CommitSha = Sha });
        var request = handler.Requests.Single();
        request.Path.Should().Be("/repos/owner/repo/commits/feature%2Fx");
        request.Accept.Should().Be("application/vnd.github.sha");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, RepositoryLookup.NotFound)]
    [InlineData(HttpStatusCode.Conflict, RepositoryLookup.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity, RepositoryLookup.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, RepositoryLookup.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway, RepositoryLookup.Failed)]
    public async Task GetBranchHeadAsync_ShouldCategorizeFailures(HttpStatusCode status, RepositoryLookup expected)
    {
        var client = Build(new RecordingHandler(_ => new HttpResponseMessage(status)));

        var head = await client.GetBranchHeadAsync("owner/repo", "main", CancellationToken.None);

        head.Should().Be(BranchHead.Missing(expected));
    }

    [Fact]
    public async Task GetBranchHeadAsync_ShouldFail_WhenTheBodyIsNotASha()
    {
        var client = Build(new RecordingHandler(_ => Text(HttpStatusCode.OK, "<html>not a sha</html>")));

        var head = await client.GetBranchHeadAsync("owner/repo", "main", CancellationToken.None);

        head.Should().Be(BranchHead.Missing(RepositoryLookup.Failed));
    }

    [Fact]
    public async Task GetTreeAsync_ShouldListEveryKnownEntryRecursively()
    {
        const string body = """
            {"sha":"t","truncated":true,"tree":[
              {"path":"src","type":"tree"},
              {"path":"src/app.cs","type":"blob","size":42},
              {"path":"vendor/lib","type":"commit"},
              {"path":"odd","type":"tag"}
            ]}
            """;
        var handler = new RecordingHandler(_ => Text(HttpStatusCode.OK, body, "application/json"));
        var client = Build(handler);

        var tree = await client.GetTreeAsync("owner/repo", Sha, CancellationToken.None);

        tree.Outcome.Should().Be(RepositoryLookup.Found);
        tree.IsTruncated.Should().BeTrue();
        tree.Entries.Should().Equal(
            new RepositoryTreeEntry { Path = "src", Type = RepositoryTreeEntryType.Tree },
            new RepositoryTreeEntry { Path = "src/app.cs", Type = RepositoryTreeEntryType.Blob, Size = 42 },
            new RepositoryTreeEntry { Path = "vendor/lib", Type = RepositoryTreeEntryType.Commit });
        var request = handler.Requests.Single();
        request.Path.Should().Be($"/repos/owner/repo/git/trees/{Sha}");
        request.Query.Should().Be("?recursive=1");
    }

    [Fact]
    public async Task ReadFileAsync_ShouldReturnTheRawBytesAtTheCommit()
    {
        var handler = new RecordingHandler(_ => Text(HttpStatusCode.OK, """{"name":"demo"}"""));
        var client = Build(handler);

        var file = await client.ReadFileAsync("owner/repo", Sha, "config/product.json", 1024, CancellationToken.None);

        file.Outcome.Should().Be(FileRead.Found);
        Encoding.UTF8.GetString(file.Content!).Should().Be("""{"name":"demo"}""");
        var request = handler.Requests.Single();
        request.Path.Should().Be("/repos/owner/repo/contents/config/product.json");
        request.Query.Should().Be($"?ref={Sha}");
        request.Accept.Should().Be("application/vnd.github.raw");
    }

    [Fact]
    public async Task ReadFileAsync_ShouldReportTooLarge_WhenTheDeclaredLengthPassesTheLimit()
    {
        var client = Build(new RecordingHandler(_ => Text(HttpStatusCode.OK, new string('x', 11))));

        var file = await client.ReadFileAsync("owner/repo", Sha, "big.txt", 10, CancellationToken.None);

        file.Should().Be(RepositoryFile.Missing(FileRead.TooLarge));
    }

    [Fact]
    public async Task ReadFileAsync_ShouldReportTooLarge_WhenAnUnsizedStreamPassesTheLimit()
    {
        var client = Build(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnseekableStream(new byte[40_000])),
        }));

        var file = await client.ReadFileAsync("owner/repo", Sha, "big.bin", 20_000, CancellationToken.None);

        file.Should().Be(RepositoryFile.Missing(FileRead.TooLarge));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, FileRead.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, FileRead.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, FileRead.Failed)]
    public async Task ReadFileAsync_ShouldCategorizeFailures(HttpStatusCode status, FileRead expected)
    {
        var client = Build(new RecordingHandler(_ => new HttpResponseMessage(status)));

        var file = await client.ReadFileAsync("owner/repo", Sha, "missing.txt", 10, CancellationToken.None);

        file.Should().Be(RepositoryFile.Missing(expected));
    }

    [Fact]
    public async Task Calls_WithoutAToken_ShouldReportUnauthorizedWithoutReachingGitHub()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Build(handler, token: null);

        (await client.GetBranchHeadAsync("owner/repo", "main", CancellationToken.None))
            .Should().Be(BranchHead.Missing(RepositoryLookup.Unauthorized));
        (await client.RepoExistsAsync("owner/repo", CancellationToken.None)).Should().Be(RepoCheck.Unauthorized);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Calls_WithoutAToken_ShouldGoAnonymous_WhenAllowed()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Build(handler, token: null, configure: o => o.AllowAnonymousCalls = true);

        (await client.RepoExistsAsync("owner/repo", CancellationToken.None)).Should().Be(RepoCheck.Exists);

        handler.Requests.Single().Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Options_ShouldSendTheProductUserAgentApiVersionAndBearerToken()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var services = Services(handler, "token", o =>
        {
            o.UserAgent = "acme-console";
            o.ApiVersion = "2022-11-28";
            o.Timeout = TimeSpan.FromSeconds(7);
        });
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IGitHubClient>().RepoExistsAsync("owner/repo", CancellationToken.None);

        var request = handler.Requests.Single();
        request.UserAgent.Should().Be("acme-console");
        request.ApiVersion.Should().Be("2022-11-28");
        request.Authorization.Should().Be("Bearer token");
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(IGitHubClient).Name)
            .Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    private static IGitHubClient Build(
        RecordingHandler handler, string? token = "token", Action<GitHubIntegrationOptions>? configure = null)
    {
        return Services(handler, token, configure).BuildServiceProvider().GetRequiredService<IGitHubClient>();
    }

    private static ServiceCollection Services(
        RecordingHandler handler, string? token, Action<GitHubIntegrationOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessTokenService>(new StaticToken(token));
        services.AddGitHubIntegration(configure).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services;
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType = "text/plain")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private sealed class StaticToken(string? token) : IAccessTokenService
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(token);
    }

    private sealed record RecordedRequest(
        string Path, string Query, string? Accept, string? Authorization, string? UserAgent, string? ApiVersion);

    /// <summary>Answers each request from a script and records what the client sent.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.Accept.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.UserAgent.ToString(),
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.Single() : null));
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A stream that hides its length, so the response carries no Content-Length.</summary>
    private sealed class UnseekableStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
