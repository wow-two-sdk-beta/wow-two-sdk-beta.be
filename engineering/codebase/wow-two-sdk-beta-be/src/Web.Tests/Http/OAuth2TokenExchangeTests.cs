using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

/// <summary>Verifies the token-exchange form, the per-session cache and the default bearer source against a fake token endpoint.</summary>
public sealed class OAuth2TokenExchangeTests
{
    private const string Audience = "https://a.test";

    [Fact]
    public async Task Form_ShouldCarryEveryExchangeField()
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }));
        rig.AddClient("a").AddOAuth2TokenExchange(options =>
        {
            OAuth2TestRig.UseCaller(options, resource: Audience, scope: "read");
            options.AdditionalParameters["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token";
        });
        await using var provider = rig.Services.BuildServiceProvider();

        using var response = await OAuth2TestRig.Client(provider, "a").GetAsync("x");

        rig.TokenEndpoint.Forms.Single().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = "person-token",
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["resource"] = Audience,
            ["scope"] = "read",
            ["client_id"] = "caller",
            ["client_secret"] = OAuth2TestRig.ClientSecret,
            ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
        });
        rig.Resource.Bearers.Should().Equal("token-1");
    }

    [Fact]
    public async Task Form_ShouldOmitScope_WhenNoneIsSet()
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }));
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();

        using var response = await OAuth2TestRig.Client(provider, "a").GetAsync("x");

        rig.TokenEndpoint.Forms.Single().Should().NotContainKey("scope");
    }

    [Fact]
    public async Task Cache_ShouldSeparateSessions_AndIgnoreASubjectTokenChangeInsideOne()
    {
        var rig = new OAuth2TestRig();
        var source = new ScriptedSource(new OAuth2SubjectToken { Token = "first-token", SessionKey = "session-1" });
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(source);
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        using var first = await client.GetAsync("x");
        source.Next = new OAuth2SubjectToken { Token = "rotated-token", SessionKey = "session-1" };
        using var sameSession = await client.GetAsync("x");
        source.Next = new OAuth2SubjectToken { Token = "other-token", SessionKey = "session-2" };
        using var otherSession = await client.GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(2);
        rig.Resource.Bearers.Should().Equal("token-1", "token-1", "token-2");
    }

    [Fact]
    public async Task Cache_ShouldNeverShareATokenWithClientCredentials()
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }));
        rig.AddClient("person").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        rig.AddClient("service").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();

        using var person = await OAuth2TestRig.Client(provider, "person").GetAsync("x");
        using var service = await OAuth2TestRig.Client(provider, "service").GetAsync("x");

        rig.Resource.Bearers.Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task Cache_ShouldFetchOnce_UnderConcurrencyInOneSession()
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.Delay = TimeSpan.FromMilliseconds(50);
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }));
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("x")));

        foreach (var response in responses)
        {
            response.Dispose();
        }

        rig.TokenEndpoint.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Fetch_ShouldSweepExpiredEntries_SoSessionsCannotGrowTheCache()
    {
        var rig = new OAuth2TestRig();
        var source = new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-0" });
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(source);
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        for (var session = 0; session < 5; session++)
        {
            source.Next = new OAuth2SubjectToken { Token = "person-token", SessionKey = $"session-{session}" };
            using var response = await client.GetAsync("x");
        }

        OAuth2TestRig.CachedTokenCount(provider).Should().Be(5);

        rig.Time.Advance(TimeSpan.FromHours(2));
        source.Next = new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-new" };
        using var next = await client.GetAsync("x");

        OAuth2TestRig.CachedTokenCount(provider).Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_ShouldEvictOnlyThatSessionsToken()
    {
        var rig = new OAuth2TestRig();
        var source = new ScriptedSource(new OAuth2SubjectToken { Token = "first-token", SessionKey = "session-1" });
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(source);
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        rig.Resource.Answer = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var rejected = await client.GetAsync("x");
        rig.Resource.Answer = null;
        source.Next = new OAuth2SubjectToken { Token = "other-token", SessionKey = "session-2" };
        using var otherSession = await client.GetAsync("x");
        rig.Resource.Answer = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        source.Next = new OAuth2SubjectToken { Token = "first-token", SessionKey = "session-1" };
        using var refetched = await client.GetAsync("x");
        rig.Resource.Answer = null;
        source.Next = new OAuth2SubjectToken { Token = "other-token", SessionKey = "session-2" };
        using var stillCached = await client.GetAsync("x");

        rig.Resource.Bearers.Should().Equal("token-1", "token-2", "token-3", "token-2");
    }

    [Fact]
    public async Task RequestCarryingAuthorization_ShouldPassThrough_WithoutReadingTheSource()
    {
        var rig = new OAuth2TestRig();
        var source = new ScriptedSource(next: null);
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(source);
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();

        using var own = new HttpRequestMessage(HttpMethod.Get, "x") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "own") } };
        using var response = await OAuth2TestRig.Client(provider, "a").SendAsync(own);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        source.Reads.Should().Be(0);
        rig.TokenEndpoint.Calls.Should().Be(0);
        rig.Resource.Bearers.Should().Equal("own");
    }

    [Fact]
    public async Task NullSource_ShouldFailLoud_NamingClientCredentials()
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(next: null));
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddOAuth2ClientCredentials*");
        rig.TokenEndpoint.Calls.Should().Be(0);
        rig.Resource.Bearers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "session-1")]
    [InlineData("person-token", "")]
    [InlineData("person-token", "  ")]
    public async Task EmptySubjectFields_ShouldFailLoud_RatherThanShareAToken(string token, string sessionKey)
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = token, SessionKey = sessionKey }));
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty Token or SessionKey*");
        rig.TokenEndpoint.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingResource_ShouldFailLoud()
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }));
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Resource is not configured*");
    }

    [Fact]
    public async Task NonSuccess_ShouldKeepTheSubjectTokenOutOfTheMessage()
    {
        var rig = new OAuth2TestRig();
        rig.Services.AddSingleton<IOAuth2SubjectTokenSource>(new ScriptedSource(new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }));
        rig.TokenEndpoint.Answer = () => OAuth2TestRig.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"bad person-token"}""");
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        var error = (await act.Should().ThrowAsync<OAuth2TokenRequestException>()).Which;
        error.Error.Should().Be("invalid_grant");
        error.Message.Should().Contain("invalid_grant").And.NotContain("person-token");
    }

    [Fact]
    public void SubjectToken_ShouldKeepTheTokenOutOfItsText()
    {
        new OAuth2SubjectToken { Token = "person-token", SessionKey = "session-1" }.ToString().Should().Contain("session-1").And.NotContain("person-token");
    }

    [Fact]
    public async Task DefaultSource_ShouldSendTheBearerToken_AndKeyByTheAuthenticatedSid()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var client = OAuth2TestRig.Client(provider, "a");

        accessor.HttpContext = Request("Bearer first-token", Authenticated(new Claim("sid", "session-1")));
        using var first = await client.GetAsync("x");
        accessor.HttpContext = Request("Bearer rotated-token", Authenticated(new Claim("sid", "session-1")));
        using var sameSession = await client.GetAsync("x");

        rig.TokenEndpoint.Forms.Single()["subject_token"].Should().Be("first-token");
        rig.Resource.Bearers.Should().Equal("token-1", "token-1");
    }

    [Fact]
    public async Task DefaultSource_ShouldNotShareAToken_AcrossAnOrganizationSwitchInsideOneSession()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var client = OAuth2TestRig.Client(provider, "a");

        accessor.HttpContext = Request("Bearer first-token", Authenticated(new Claim("sid", "session-1"), new Claim("org_id", "org-a")));
        using var first = await client.GetAsync("x");
        accessor.HttpContext = Request("Bearer second-token", Authenticated(new Claim("sid", "session-1"), new Claim("org_id", "org-b")));
        using var second = await client.GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(2);
        rig.Resource.Bearers.Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task DefaultSource_ShouldKeyByTheTokenHash_WhenThereIsNoSid()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = Request("Bearer person-token", Authenticated());

        var subject = await provider.GetRequiredService<IOAuth2SubjectTokenSource>().GetAsync(CancellationToken.None);

        subject!.Token.Should().Be("person-token");
        subject.SessionKey.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("person-token"))));
    }

    [Fact]
    public async Task DefaultSource_ShouldIgnoreTheSidOfAnUnauthenticatedUser()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = Request(
            "Bearer person-token",
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", "forged")])));

        var subject = await provider.GetRequiredService<IOAuth2SubjectTokenSource>().GetAsync(CancellationToken.None);

        subject!.SessionKey.Should().NotBe("forged");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    public async Task DefaultSource_ShouldReturnNull_WithoutABearerToken(string? authorization)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = Request(authorization, Authenticated());

        var subject = await provider.GetRequiredService<IOAuth2SubjectTokenSource>().GetAsync(CancellationToken.None);

        subject.Should().BeNull();
    }

    [Fact]
    public async Task DefaultSource_ShouldFailLoud_OutsideARequest()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2TokenExchange(options => OAuth2TestRig.UseCaller(options, resource: Audience));
        await using var provider = rig.Services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddOAuth2ClientCredentials*");
    }

    private static DefaultHttpContext Request(string? authorization, ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext { User = user };
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return context;
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    private sealed class ScriptedSource(OAuth2SubjectToken? next) : IOAuth2SubjectTokenSource
    {
        public OAuth2SubjectToken? Next { get; set; } = next;

        public int Reads { get; private set; }

        public ValueTask<OAuth2SubjectToken?> GetAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult(Next);
        }
    }
}
