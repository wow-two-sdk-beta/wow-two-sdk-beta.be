using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

/// <summary>Wires named clients to a fake token endpoint and a fake resource, on a fake clock.</summary>
internal sealed class OAuth2TestRig
{
    public const string ClientSecret = "client-secret-value";
    public static readonly Uri TokenUri = new("https://identity.test/connect/token");

    public OAuth2TestRig()
    {
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(() => TokenEndpoint);
    }

    public FakeTokenEndpoint TokenEndpoint { get; } = new();

    public FakeResource Resource { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public ServiceCollection Services { get; } = new();

    public IHttpClientBuilder AddClient(string name) => Services
        .AddHttpClient(name, client => client.BaseAddress = new Uri("https://resource.test/"))
        .ConfigurePrimaryHttpMessageHandler(() => Resource);

    public static void UseCaller(OAuth2TokenClientOptions options, string? resource = null, string? scope = null)
    {
        options.TokenEndpoint = TokenUri;
        options.ClientId = "caller";
        options.ClientSecret = ClientSecret;
        options.Resource = resource;
        options.Scope = scope;
    }

    public static HttpClient Client(IServiceProvider provider, string name) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);

    /// <summary>Reads the cache's size; the cache has no public seam, and the no-growth rule needs a guard.</summary>
    public static int CachedTokenCount(IServiceProvider provider)
    {
        var repositoryType = typeof(OAuth2HttpClientNames).Assembly
            .GetType("WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2.OAuth2TokenRepository", throwOnError: true)!;
        var tokens = repositoryType.GetField("_tokens", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(provider.GetRequiredService(repositoryType));
        return ((ICollection)tokens!).Count;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Issues <c>token-1</c>, <c>token-2</c> and so on, and records every form it receives.</summary>
    internal sealed class FakeTokenEndpoint : HttpMessageHandler
    {
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> _forms = new();
        private int _issued;

        public TimeSpan Delay { get; set; }

        public int ExpiresInSeconds { get; set; } = 3600;

        public Func<HttpResponseMessage>? Answer { get; set; }

        public IReadOnlyList<IReadOnlyDictionary<string, string>> Forms => [.. _forms];

        public int Calls => _forms.Count;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _forms.Enqueue(QueryHelpers.ParseQuery(body).ToDictionary(field => field.Key, field => field.Value.ToString()));
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return Answer?.Invoke()
                ?? Json(HttpStatusCode.OK, $$"""{"access_token":"token-{{Interlocked.Increment(ref _issued)}}","expires_in":{{ExpiresInSeconds}}}""");
        }
    }

    /// <summary>Records the bearer token of every request; answers <c>200</c> unless a test scripts otherwise.</summary>
    internal sealed class FakeResource : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string?> _bearers = new();

        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Answer { get; set; }

        public IReadOnlyList<string?> Bearers => [.. _bearers];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _bearers.Enqueue(request.Headers.Authorization?.Parameter);
            return Answer?.Invoke(request) ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
