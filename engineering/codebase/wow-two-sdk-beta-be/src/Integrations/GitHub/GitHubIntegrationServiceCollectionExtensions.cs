using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Backend.Beta.Foundation.Options;

namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>GitHub integration registration.</summary>
public static class GitHubIntegrationServiceCollectionExtensions
{
    private const string ApiBaseAddress = "https://api.github.com/";
    private const string AcceptMediaType = "application/vnd.github+json";
    private const string ApiVersionHeader = "X-GitHub-Api-Version";

    /// <summary>Registers a typed <see cref="HttpClient"/> for the GitHub REST API (base <c>https://api.github.com/</c>) bound to <see cref="IGitHubClient"/>. Each call carries a Bearer token from <see cref="IAccessTokenService"/> — register one (e.g. <c>AddHttpContextAccessTokenProvider</c>).</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">User-Agent, API version, timeout and anonymous-call policy; the host section <c>Integrations:GitHub</c> is applied after it.</param>
    public static IHttpClientBuilder AddGitHubIntegration(
        this IServiceCollection services,
        Action<GitHubIntegrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModuleOptions(
            GitHubIntegrationOptions.SectionName,
            configure,
            builder => builder
                .Validate(o => !string.IsNullOrWhiteSpace(o.UserAgent), "GitHubIntegrationOptions.UserAgent must not be empty.")
                .Validate(o => o.ApiVersion is null || !string.IsNullOrWhiteSpace(o.ApiVersion), "GitHubIntegrationOptions.ApiVersion must not be blank when supplied.")
                .Validate(o => o.Timeout is null || o.Timeout > TimeSpan.Zero, "GitHubIntegrationOptions.Timeout must be positive when supplied."));

        return services.AddHttpClient<IGitHubClient, GitHubClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<GitHubIntegrationOptions>();
            client.BaseAddress = new Uri(ApiBaseAddress);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(AcceptMediaType));
            if (options.ApiVersion is not null)
                client.DefaultRequestHeaders.Add(ApiVersionHeader, options.ApiVersion);
            if (options.Timeout is { } timeout)
                client.Timeout = timeout;
        });
    }
}
