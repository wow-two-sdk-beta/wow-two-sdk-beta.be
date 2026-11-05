using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

/// <summary>Verifies the client-credentials form, the shared cache and the failure paths against a fake token endpoint.</summary>
public sealed class OAuth2ClientCredentialsTests
{
    [Fact]
    public async Task ColdCache_ShouldFetchOneToken_UnderConcurrency()
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.Delay = TimeSpan.FromMilliseconds(50);
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("x")));

        foreach (var response in responses)
        {
            response.Dispose();
        }

        rig.TokenEndpoint.Calls.Should().Be(1);
        rig.Resource.Bearers.Should().HaveCount(20).And.AllBe("token-1");
    }

    [Fact]
    public async Task TwoAudiences_ShouldGetTwoTokens()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: "https://a.test"));
        rig.AddClient("b").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: "https://b.test"));
        await using var provider = rig.Services.BuildServiceProvider();

        using var first = await OAuth2TestRig.Client(provider, "a").GetAsync("x");
        using var second = await OAuth2TestRig.Client(provider, "b").GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(2);
        rig.Resource.Bearers.Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task TwoNamedClients_ForOneAudience_ShouldShareOneToken()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: "https://a.test", scope: "read"));
        rig.AddClient("b").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: "https://a.test", scope: "read"));
        await using var provider = rig.Services.BuildServiceProvider();

        using var first = await OAuth2TestRig.Client(provider, "a").GetAsync("x");
        using var second = await OAuth2TestRig.Client(provider, "b").GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(1);
        rig.Resource.Bearers.Should().Equal("token-1", "token-1");
    }

    [Fact]
    public async Task CacheKey_ShouldIgnoreParameterOrder_ButNotParameterValues()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options =>
        {
            OAuth2TestRig.UseCaller(options);
            options.AdditionalParameters["one"] = "1";
            options.AdditionalParameters["two"] = "2";
        });
        rig.AddClient("b").AddOAuth2ClientCredentials(options =>
        {
            OAuth2TestRig.UseCaller(options);
            options.AdditionalParameters["two"] = "2";
            options.AdditionalParameters["one"] = "1";
        });
        rig.AddClient("c").AddOAuth2ClientCredentials(options =>
        {
            OAuth2TestRig.UseCaller(options);
            options.AdditionalParameters["one"] = "1";
            options.AdditionalParameters["two"] = "other";
        });
        await using var provider = rig.Services.BuildServiceProvider();

        using var first = await OAuth2TestRig.Client(provider, "a").GetAsync("x");
        using var second = await OAuth2TestRig.Client(provider, "b").GetAsync("x");
        using var third = await OAuth2TestRig.Client(provider, "c").GetAsync("x");

        rig.Resource.Bearers.Should().Equal("token-1", "token-1", "token-2");
    }

    [Fact]
    public async Task CacheKey_ShouldKeepFieldBoundaries()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: "r\nb", scope: "c"));
        rig.AddClient("b").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options, resource: "r", scope: "b\nc"));
        await using var provider = rig.Services.BuildServiceProvider();

        using var first = await OAuth2TestRig.Client(provider, "a").GetAsync("x");
        using var second = await OAuth2TestRig.Client(provider, "b").GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Form_ShouldSendResourceAndScope_AndKeepAdditionalParameters()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options =>
        {
            OAuth2TestRig.UseCaller(options, resource: "https://a.test", scope: "read write");
            options.AdditionalParameters["audience"] = "legacy";
        });
        await using var provider = rig.Services.BuildServiceProvider();

        using var response = await OAuth2TestRig.Client(provider, "a").GetAsync("x");

        rig.TokenEndpoint.Forms.Single().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "caller",
            ["client_secret"] = OAuth2TestRig.ClientSecret,
            ["resource"] = "https://a.test",
            ["scope"] = "read write",
            ["audience"] = "legacy",
        });
    }

    [Fact]
    public async Task Form_ShouldOmitResource_WhenNoneIsSet()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();

        using var response = await OAuth2TestRig.Client(provider, "a").GetAsync("x");

        rig.TokenEndpoint.Forms.Single().Should().NotContainKey("resource");
    }

    [Theory]
    [InlineData(3600, 3539)] // skew is the configured 60 s
    [InlineData(60, 29)] // skew is capped at half the lifetime
    public async Task Token_ShouldRefreshAfterExpiryMinusSkew(int expiresIn, int lastCachedSecond)
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.ExpiresInSeconds = expiresIn;
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        using var first = await client.GetAsync("x");
        rig.Time.Advance(TimeSpan.FromSeconds(lastCachedSecond));
        using var cached = await client.GetAsync("x");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        using var refreshed = await client.GetAsync("x");

        rig.Resource.Bearers.Should().Equal("token-1", "token-1", "token-2");
    }

    [Fact]
    public async Task Unauthorized_ShouldEvictTheToken_WithoutRetrying()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        rig.Resource.Answer = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        using var rejected = await client.GetAsync("x");

        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        rig.Resource.Bearers.Should().Equal("token-1");

        rig.Resource.Answer = null;
        using var next = await client.GetAsync("x");

        next.StatusCode.Should().Be(HttpStatusCode.OK);
        rig.Resource.Bearers.Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task LateUnauthorized_ShouldKeepTheTokenThatReplacedTheRejectedOne()
    {
        var rig = new OAuth2TestRig();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Resource.Answer = request =>
        {
            if (request.Headers.Authorization!.Parameter != "token-1")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            arrived.TrySetResult();
            return held.Task;
        };
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        var stale = client.GetAsync("x");
        await arrived.Task;
        rig.Time.Advance(TimeSpan.FromHours(2));
        using var fresh = await client.GetAsync("x");
        held.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var rejected = await stale;
        using var next = await client.GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(2);
        rig.Resource.Bearers.Should().Equal("token-1", "token-2", "token-2");
    }

    [Fact]
    public async Task RequestCarryingAuthorization_ShouldPassThrough_AndEvictNothing()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();
        var client = OAuth2TestRig.Client(provider, "a");

        using var cached = await client.GetAsync("x");
        rig.Resource.Answer = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var own = new HttpRequestMessage(HttpMethod.Get, "x") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "own") } };
        using var rejected = await client.SendAsync(own);
        rig.Resource.Answer = null;
        using var next = await client.GetAsync("x");

        rig.TokenEndpoint.Calls.Should().Be(1);
        rig.Resource.Bearers.Should().Equal("token-1", "own", "token-1");
    }

    [Fact]
    public async Task NonSuccess_ShouldThrowTheRequestException_WithItsFields_AndACleanMessage()
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.Answer = () => OAuth2TestRig.Json(
            HttpStatusCode.BadRequest,
            $$"""{"error":"invalid_client","error_description":"rejected {{OAuth2TestRig.ClientSecret}}"}""");
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        var error = (await act.Should().ThrowAsync<OAuth2TokenRequestException>()).Which;
        error.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        error.Error.Should().Be("invalid_client");
        error.ErrorDescription.Should().Be($"rejected {OAuth2TestRig.ClientSecret}");
        error.Message.Should().Contain("400").And.Contain("invalid_client").And.NotContain(OAuth2TestRig.ClientSecret);
    }

    [Fact]
    public async Task NonSuccess_ShouldKeepAnUnregisteredErrorOutOfTheMessage()
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.Answer = () => OAuth2TestRig.Json(HttpStatusCode.BadRequest, $$"""{"error":"{{OAuth2TestRig.ClientSecret}}"}""");
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        var error = (await act.Should().ThrowAsync<OAuth2TokenRequestException>()).Which;
        error.Error.Should().Be(OAuth2TestRig.ClientSecret);
        error.Message.Should().NotContain(OAuth2TestRig.ClientSecret);
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("""["not","an","object"]""")]
    [InlineData("""{"error":42}""")]
    public async Task NonSuccess_ShouldLeaveTheFieldsNull_WhenTheBodyHasNone(string body)
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.Answer = () => OAuth2TestRig.Json(HttpStatusCode.BadGateway, body);
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        var error = (await act.Should().ThrowAsync<OAuth2TokenRequestException>()).Which;
        error.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        error.Error.Should().BeNull();
        error.ErrorDescription.Should().BeNull();
    }

    [Fact]
    public async Task SuccessWithoutAccessToken_ShouldKeepThrowingAnAppException()
    {
        var rig = new OAuth2TestRig();
        rig.TokenEndpoint.Answer = () => OAuth2TestRig.Json(HttpStatusCode.OK, """{"expires_in":3600}""");
        rig.AddClient("a").AddOAuth2ClientCredentials(options => OAuth2TestRig.UseCaller(options));
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        await act.Should().ThrowAsync<AppException>().WithMessage("*has no 'access_token'*");
    }

    [Fact]
    public async Task MissingTokenEndpoint_ShouldFailLoud()
    {
        var rig = new OAuth2TestRig();
        rig.AddClient("a").AddOAuth2ClientCredentials(options => options.ClientId = "caller");
        await using var provider = rig.Services.BuildServiceProvider();

        var act = () => OAuth2TestRig.Client(provider, "a").GetAsync("x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TokenEndpoint is not configured*");
    }
}
