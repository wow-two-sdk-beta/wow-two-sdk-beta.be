namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>Holds the GitHub REST client's request headers, timeout and anonymous-call policy.</summary>
public sealed record GitHubIntegrationOptions
{
    /// <summary>The host configuration section applied after code configuration.</summary>
    public const string SectionName = "Integrations:GitHub";

    /// <summary>The product name sent as <c>User-Agent</c>; GitHub rejects calls without one. Default <c>wow-two-sdk</c>.</summary>
    public string UserAgent { get; set; } = "wow-two-sdk";

    /// <summary>The REST API version sent as <c>X-GitHub-Api-Version</c> (e.g. <c>2022-11-28</c>); <see langword="null"/> sends none and takes GitHub's default.</summary>
    public string? ApiVersion { get; set; }

    /// <summary>The per-call timeout; <see langword="null"/> keeps the <see cref="HttpClient"/> default of 100 seconds.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Whether a call proceeds without an <c>Authorization</c> header when the token service yields no token. Default <see langword="false"/>: such a call reports <c>Unauthorized</c> without reaching GitHub.</summary>
    /// <remarks>Anonymous calls see public repositories only and share GitHub's low unauthenticated rate limit.</remarks>
    public bool AllowAnonymousCalls { get; set; }
}
