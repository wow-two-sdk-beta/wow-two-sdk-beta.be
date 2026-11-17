using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.Instagram;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Integrations.Instagram;

/// <summary>Exercises real provider requests over an in-memory transport, without tokens or network access.</summary>
public sealed class InstagramClientTests
{
    private static readonly InstagramMediaRequest Request = new()
    {
        ProfessionalAccountId = "123456", ApiVersion = "v27.0", Username = "publisher", PageSize = 2
    };

    [Fact]
    public async Task CursorOnlyPage_ShouldContinue_AndStripPagingUrlsAndUnrequestedFields()
    {
        const string media = """
            [{"id":"1","caption":"first","media_type":"CAROUSEL_ALBUM","unknown":"dropped",
              "children":{"data":[{"id":"2","media_type":"IMAGE","unknown":"dropped"}],
              "paging":{"next":"https://other.invalid/?access_token=synthetic"}}}]
            """;
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(Page(media,
            """{"cursors":{"after":"next_cursor+="},"next":"https://other.invalid/?access_token=synthetic"}"""))));
        var tokens = new MutableTokenSource("synthetic-token");
        var client = Create(handler, tokens);

        InstagramMediaPage result = Success(await client.GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal("next_cursor+=", result.After);
        Assert.Equal("first", result.Media[0].GetProperty("caption").GetString());
        Assert.False(result.Media[0].TryGetProperty("unknown", out _));
        Assert.False(result.Media[0].GetProperty("children").TryGetProperty("paging", out _));
        Assert.Equal("https://graph.facebook.com/v27.0/123456", result.Diagnostics.RequestUrl);
        Assert.Equal(200, result.Diagnostics.HttpStatus);
        Assert.Equal(1, result.Diagnostics.Requests);
        Assert.True(result.Diagnostics.Bytes > 0);
        Assert.Equal("Bearer synthetic-token", handler.Authorization.Single());
        Assert.DoesNotContain("synthetic-token", handler.Uris.Single());
        Assert.Equal(1, tokens.Calls);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task CursorWithoutNextUrl_ShouldDriveSecondRequest_AndEmptyPageShouldStop()
    {
        int call = 0;
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(++call == 1
            ? Page("[{\"id\":\"1\"}]", "{\"cursors\":{\"after\":\"last_cursor\"}}")
            : Page("[]"))));
        var client = Create(handler);

        InstagramMediaPage first = Success(await client.GetBusinessDiscoveryMediaAsync(Request));
        InstagramMediaPage terminal = Success(await client.GetBusinessDiscoveryMediaAsync(Request with { After = first.After }));

        Assert.Equal("last_cursor", first.After);
        Assert.Null(terminal.After);
        Assert.Empty(terminal.Media);
        Assert.Contains(".after(last_cursor)", Uri.UnescapeDataString(handler.Uris[1]));
        Assert.DoesNotContain("last_cursor", terminal.Diagnostics.RequestUrl!);
        Assert.Equal(2, handler.Uris.Count);
    }

    [Theory]
    [InlineData("{\"cursors\":{\"after\":\"previous\"}}")]
    [InlineData("{\"cursors\":{\"after\":42}}")]
    public async Task EmptyTerminalPage_ShouldIgnoreStalePagingMetadata(string paging)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(Page("[]", paging))));

        InstagramMediaPage result = Success(await Create(handler).GetBusinessDiscoveryMediaAsync(Request with { After = "previous" }));

        Assert.Null(result.After);
        Assert.Empty(result.Media);
    }

    [Theory]
    [InlineData("{\"cursors\":{\"after\":\"previous\"}}")]
    [InlineData("{\"cursors\":{\"after\":\"bad)cursor\"}}")]
    [InlineData("{\"cursors\":{\"after\":\"abc\\n\"}}")]
    [InlineData("{\"cursors\":{\"after\":42}}")]
    [InlineData("{\"cursors\":[]}")]
    [InlineData("{\"next\":\"https://other.invalid\"}")]
    [InlineData("[]")]
    public async Task InvalidOrRepeatedCursor_ShouldFailWithoutFollowingProviderUrls(string paging)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(Page("[{\"id\":\"1\"}]", paging))));

        InstagramFailure failure = Failure(await Create(handler).GetBusinessDiscoveryMediaAsync(Request with { After = "previous" }));

        Assert.Equal(InstagramFailureKind.UnexpectedContent, failure.Kind);
        Assert.Single(handler.Uris);
        Assert.Equal(200, failure.Diagnostics.HttpStatus);
        Assert.True(failure.Diagnostics.Bytes > 0);
    }

    [Theory]
    [InlineData("publisher){}", "v27.0", "123", null, 2)]
    [InlineData("publisher\n", "v27.0", "123", null, 2)]
    [InlineData("publisher", "v27.0/../", "123", null, 2)]
    [InlineData("publisher", "v27.0", "123?", null, 2)]
    [InlineData("publisher", "v27.0", "123", "cursor)", 2)]
    [InlineData("publisher", "v27.0", "123", "", 2)]
    [InlineData("publisher", "v27.0", "123", null, 51)]
    public async Task InvalidRequest_ShouldFailBeforeObtainingCredentials(string username, string version,
        string account, string? cursor, int pageSize)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(Page("[]"))));
        var tokens = new MutableTokenSource("synthetic-token");

        InstagramFailure failure = Failure(await Create(handler, tokens).GetBusinessDiscoveryMediaAsync(Request with
        {
            Username = username, ApiVersion = version, ProfessionalAccountId = account, After = cursor, PageSize = pageSize
        }));

        Assert.Equal(InstagramFailureKind.InvalidRequest, failure.Kind);
        Assert.Equal(0, tokens.Calls);
        Assert.Empty(handler.Uris);
        Assert.Equal(0, failure.Diagnostics.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid token")]
    [InlineData("line\nbreak")]
    public async Task MissingOrInvalidToken_ShouldFailLocally(string? token)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(Page("[]"))));

        InstagramFailure failure = Failure(await Create(handler, new MutableTokenSource(token)).GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(InstagramFailureKind.CredentialsMissing, failure.Kind);
        Assert.Empty(handler.Uris);
        Assert.Null(failure.Diagnostics.RequestUrl);
    }

    [Theory]
    [InlineData(190, InstagramFailureKind.LoginRequired)]
    [InlineData(4, InstagramFailureKind.RateLimited)]
    [InlineData(17, InstagramFailureKind.RateLimited)]
    [InlineData(32, InstagramFailureKind.RateLimited)]
    [InlineData(613, InstagramFailureKind.RateLimited)]
    [InlineData(80004, InstagramFailureKind.RateLimited)]
    [InlineData(10, InstagramFailureKind.Restricted)]
    [InlineData(100, InstagramFailureKind.Restricted)]
    [InlineData(200, InstagramFailureKind.Restricted)]
    [InlineData(999, InstagramFailureKind.ProviderFailed)]
    public async Task ProviderErrors_ShouldPreserveOnlyNumericCodeAndSafeDiagnostics(int code, InstagramFailureKind expected)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            JsonSerializer.Serialize(new { error = new { code, message = "synthetic-token-secret", error_user_msg = "secret" } }),
            HttpStatusCode.BadRequest)));

        InstagramFailure failure = Failure(await Create(handler).GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(expected, failure.Kind);
        Assert.Equal(code, failure.Diagnostics.ProviderCode);
        Assert.Equal(400, failure.Diagnostics.HttpStatus);
        Assert.Equal(1, failure.Diagnostics.Requests);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(failure));
        if (expected == InstagramFailureKind.RateLimited)
            Assert.Equal(TimeSpan.FromMinutes(15), failure.Diagnostics.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, InstagramFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, InstagramFailureKind.LoginRequired)]
    [InlineData(HttpStatusCode.Forbidden, InstagramFailureKind.Restricted)]
    [InlineData(HttpStatusCode.BadGateway, InstagramFailureKind.HttpError)]
    [InlineData(HttpStatusCode.Redirect, InstagramFailureKind.InvalidRedirect)]
    public async Task StatusFailures_ShouldSurviveNonJsonBodies(HttpStatusCode status, InstagramFailureKind expected)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json("<html>private-error</html>", status)));

        InstagramFailure failure = Failure(await Create(handler).GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(expected, failure.Kind);
        Assert.Equal((int)status, failure.Diagnostics.HttpStatus);
        Assert.Equal(1, failure.Diagnostics.Requests);
        Assert.DoesNotContain("private-error", JsonSerializer.Serialize(failure));
        Assert.Single(handler.Uris);
    }

    [Fact]
    public async Task RetryDate_ShouldUseInjectedClock_AndClampPastDate()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var handler = new RecordingHandler((_, _) =>
        {
            HttpResponseMessage response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddMinutes(-1));
            return Task.FromResult(response);
        });

        InstagramFailure failure = Failure(await Create(handler, clock: clock).GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(TimeSpan.Zero, failure.Diagnostics.RetryAfter);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResponseLimit_ShouldBoundKnownAndStreamingBodies(bool knownLength)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = knownLength ? new StringContent(new string('x', 2048)) : new UnknownLengthContent(new string('x', 2048))
        }));

        InstagramFailure failure = Failure(await Create(handler, options: Options() with { MaximumResponseBytes = 1024 })
            .GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(InstagramFailureKind.ResponseTooLarge, failure.Kind);
        Assert.Equal(knownLength ? 0 : 1025, failure.Diagnostics.Bytes);
        Assert.Equal(200, failure.Diagnostics.HttpStatus);
    }

    [Theory]
    [InlineData("{\"nested\":{\"a\":{\"b\":{\"c\":{\"d\":{}}}}}}")]
    [InlineData("{\"business_discovery\":{\"username\":\"different\",\"media\":{\"data\":[]}}}")]
    [InlineData("{\"business_discovery\":{\"username\":\"publisher\",\"media\":{\"data\":[{}]}}}")]
    public async Task InvalidDepthOrMediaShape_ShouldReturnSafeFailure(string body)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(body)));

        InstagramFailure failure = Failure(await Create(handler, options: Options() with { MaximumJsonDepth = 4 })
            .GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(InstagramFailureKind.UnexpectedContent, failure.Kind);
    }

    [Fact]
    public async Task Deadline_ShouldBoundTheWholeCall_AndCallerCancellationShouldPropagate()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(Page("[]"));
        });
        var client = Create(handler, options: Options() with { Timeout = TimeSpan.FromMilliseconds(30) });

        InstagramFailure failure = Failure(await client.GetBusinessDiscoveryMediaAsync(Request));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.Equal(InstagramFailureKind.Timeout, failure.Kind);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetBusinessDiscoveryMediaAsync(Request, cancellation.Token));
        Assert.Single(handler.Uris);
    }

    [Fact]
    public async Task TransportFailure_ShouldNotRepeatExceptionText()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("synthetic-token-secret"));

        InstagramFailure failure = Failure(await Create(handler).GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(InstagramFailureKind.NetworkError, failure.Kind);
        Assert.Equal(1, failure.Diagnostics.Requests);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(failure));
    }

    [Theory]
    [InlineData("http://graph.facebook.com/")]
    [InlineData("https://graph.facebook.com.evil.invalid/")]
    [InlineData("https://graph.facebook.com:8443/")]
    [InlineData("https://secret@graph.facebook.com/")]
    [InlineData("https://graph.facebook.com/?access_token=secret")]
    [InlineData("https://graph.facebook.com/v27.0/")]
    public void UnsafeOrigin_ShouldFailBeforeClientConstruction(string address)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(Page("[]"))));

        Assert.Throws<ArgumentException>(() => Create(handler, options: Options() with { BaseAddress = new Uri(address) }));
        Assert.Empty(handler.Uris);
    }

    [Fact]
    public async Task Registration_ShouldIsolateCredentials_ResolveEachCall_AndNeverRetryThrottle()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json("{}", HttpStatusCode.TooManyRequests)));
        var selected = new MutableTokenSource("selected-one");
        var unrelated = new MutableTokenSource("wrong-provider");
        var services = new ServiceCollection();
        services.AddSingleton<IAccessTokenService>(unrelated);
        services.AddInstagramIntegration(_ => selected, options => options.BaseAddress = new Uri("https://graph.facebook.com/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IInstagramClient client = provider.GetRequiredService<IInstagramClient>();

        InstagramFailure first = Failure(await client.GetBusinessDiscoveryMediaAsync(Request));
        selected.Token = "selected-two";
        InstagramFailure second = Failure(await client.GetBusinessDiscoveryMediaAsync(Request));

        Assert.Equal(InstagramFailureKind.RateLimited, first.Kind);
        Assert.Equal(InstagramFailureKind.RateLimited, second.Kind);
        Assert.Equal(2, handler.Uris.Count);
        Assert.Equal(["Bearer selected-one", "Bearer selected-two"], handler.Authorization);
        Assert.Equal(2, selected.Calls);
        Assert.Equal(0, unrelated.Calls);
    }

    [Fact]
    public void Registration_ShouldValidateMissingBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddInstagramIntegration(_ => new MutableTokenSource("synthetic"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IInstagramClient>());
    }

    private static InstagramMediaPage Success(Result<InstagramMediaPage, InstagramFailure> result)
        => Assert.IsType<Result<InstagramMediaPage, InstagramFailure>.Success>(result).Value;
    private static InstagramFailure Failure(Result<InstagramMediaPage, InstagramFailure> result)
        => Assert.IsType<Result<InstagramMediaPage, InstagramFailure>.Failure>(result).Error;
    private static InstagramIntegrationOptions Options() => new() { BaseAddress = new Uri("https://graph.facebook.com/") };
    private static InstagramClient Create(RecordingHandler handler, IAccessTokenService? tokens = null,
        InstagramIntegrationOptions? options = null, TimeProvider? clock = null)
        => new(new HttpClient(handler), tokens ?? new MutableTokenSource("synthetic-token"), options ?? Options(), clock ?? TimeProvider.System);
    private static string Page(string media, string? paging = null)
        => "{\"business_discovery\":{\"username\":\"publisher\",\"media\":{\"data\":" + media
            + (paging is null ? "" : ",\"paging\":" + paging) + "}}}";
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class MutableTokenSource(string? token) : IAccessTokenService
    {
        public string? Token { get; set; } = token;
        public int Calls { get; private set; }
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Token);
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];
        public List<string?> Authorization { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.AbsoluteUri);
            Authorization.Add(request.Headers.Authorization?.ToString());
            HttpResponseMessage result = await response(request, cancellationToken);
            result.RequestMessage = request;
            return result;
        }
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));
    }
}
