using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Http;
using WoW.Two.Sdk.Beta.Be.Testing.Integrations;
using WoW.Two.Sdk.Beta.Be.Testing.Web;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Testing;

/// <summary>The scripted HTTP fakes drive a real outbound client; the problem reader parses an SDK problem.</summary>
public sealed class ScriptedHttpTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task ScriptedHandler_ShouldAnswerByMethodAndPath_WithAnyStatus_AndRecordTheHost()
    {
        var handler = new ScriptedHttpMessageHandler();
        var issued = 0;
        handler.Answers["POST app/installations/7/access_tokens"] =
            () => ScriptedHttpMessageHandler.JsonAnswer(HttpStatusCode.Created, $$"""{"token":"ghs_{{++issued}}"}""");
        handler.Failures["app/installations/7/access_tokens"] = HttpStatusCode.Forbidden;
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };

        using var first = await client.PostAsync("app/installations/7/access_tokens?ignored=1", content: null);
        using var second = await client.PostAsync("app/installations/7/access_tokens", content: null);
        using var read = await client.GetAsync("app/installations/7/access_tokens");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        (await second.Content.ReadAsStringAsync()).Should().Be("""{"token":"ghs_2"}""");
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden, "an answer keys on the method; the GET falls through to the path scripts");
        handler.CountOfRoute("POST app/installations/7/access_tokens").Should().Be(2);
        handler.Requests.Should().AllSatisfy(request => request.Host.Should().Be("api.github.com"));
        handler.Requests[0].Route.Should().Be("POST app/installations/7/access_tokens");
        handler.Requests[0].PathAndQuery.Should().Be("app/installations/7/access_tokens?ignored=1");
    }

    [Fact]
    public async Task ScriptedHandler_ShouldAnswerFromTheScriptAndRecordEveryRequest()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Raw[$"repos/o/r/contents/docs/a%20b.md?ref={Sha}"] = Encoding.UTF8.GetBytes("# notes");
        handler.Failures[$"repos/o/r/contents/locked.md?ref={Sha}"] = HttpStatusCode.Forbidden;
        var client = GitHub(handler, new FixedAccessTokenService("token"));

        var found = await client.ReadFileAsync("o/r", Sha, "docs/a b.md", 1024, CancellationToken.None);
        var locked = await client.ReadFileAsync("o/r", Sha, "locked.md", 1024, CancellationToken.None);
        var missing = await client.ReadFileAsync("o/r", Sha, "nope.md", 1024, CancellationToken.None);

        Encoding.UTF8.GetString(found.Content!).Should().Be("# notes");
        locked.Outcome.Should().Be(FileRead.Unauthorized);
        missing.Outcome.Should().Be(FileRead.NotFound);
        handler.Requests.Should().HaveCount(3).And.OnlyContain(request => request.Authorization == "Bearer token");
        handler.Requests[0].Headers["Accept"].Should().Be("application/vnd.github.raw");
        handler.CountOf($"repos/o/r/contents/locked.md?ref={Sha}").Should().Be(1);
    }

    [Fact]
    public async Task SingleClientFactory_ShouldShareTheHandlerAcrossNamesAndDisposals()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Json["status"] = """{"ok":true}""";
        var factory = new SingleClientHttpClientFactory(handler, new Uri("https://api.example.test/"));

        using (var first = factory.CreateClient("first"))
            (await first.GetStringAsync(new Uri("status", UriKind.Relative))).Should().Be("""{"ok":true}""");
        using var second = factory.CreateClient("second");
        using var response = await second.GetAsync(new Uri("missing", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Names.Should().Equal("first", "second");
        handler.Requests.Select(request => request.PathAndQuery).Should().Equal("status", "missing");
    }

    [Fact]
    public async Task FixedAccessTokenService_WithoutAToken_ShouldDriveTheUnauthorizedBranch()
    {
        var handler = new ScriptedHttpMessageHandler();
        var tokens = new FixedAccessTokenService(null);

        var head = await GitHub(handler, tokens).GetBranchHeadAsync("o/r", "main", CancellationToken.None);

        head.Outcome.Should().Be(RepositoryLookup.Unauthorized);
        tokens.Calls.Should().Be(1);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadProblemAsync_ShouldParseAnSdkProblem()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAppExceptionHandling();
        await using var app = builder.Build();
        app.MapGet("/missing", (HttpContext context) =>
        {
            var problem = context.RequestServices.GetRequiredService<IAppErrorProblemDetailsFactory>()
                .Create(AppErrorFactory.NotFound("No such item."), context);
            return Results.Problem(problem);
        });
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync(new Uri("/missing", UriKind.Relative));
        var problem = await response.ReadProblemAsync();

        problem.Status.Should().Be(404);
        problem.Detail.Should().Be("No such item.");
        problem.Extensions["code"].Should().NotBeNull();
        problem.Extensions["code"]!.ToString().Should().Be(nameof(AppErrorType.NotFound));
    }

    [Fact]
    public async Task ReadProblemAsync_ShouldThrow_WhenTheBodyIsEmpty()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(string.Empty) };

        await FluentActions.Awaiting(response.ReadProblemAsync).Should().ThrowAsync<InvalidOperationException>();
    }

    private static IGitHubClient GitHub(ScriptedHttpMessageHandler handler, IAccessTokenService tokens)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tokens);
        services.AddGitHubIntegration().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IGitHubClient>();
    }
}
