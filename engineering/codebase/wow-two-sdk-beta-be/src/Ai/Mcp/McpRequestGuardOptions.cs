namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Holds the request screen every endpoint <see cref="McpEndpointRouteBuilderExtensions.MapAuthenticatedMcp"/> maps applies:
/// which browser origins may call it and how large a request body it reads.</summary>
public sealed record McpRequestGuardOptions
{
    /// <summary>Holds the default body limit, 64 KiB — an <c>initialize</c> with the client's capabilities or a tool call with
    /// a few arguments stays within a few KiB.</summary>
    public const long DefaultMaxRequestBodyBytes = 64 * 1024;

    /// <summary>Gets or sets the browser origins allowed besides the endpoint's own, each <c>scheme://host[:port]</c> over http or
    /// https. Empty by default, so only the endpoint's own origin and requests without an <c>Origin</c> (CLI clients) pass.</summary>
    public IList<string> AllowedOrigins { get; set; } = [];

    /// <summary>Gets or sets the largest request body read, in bytes; a larger one gets 413. <see langword="null"/> lifts the limit.
    /// Defaults to <see cref="DefaultMaxRequestBodyBytes"/>.</summary>
    public long? MaxRequestBodyBytes { get; set; } = DefaultMaxRequestBodyBytes;
}
