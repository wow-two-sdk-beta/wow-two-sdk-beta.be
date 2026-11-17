namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Describes one provider request without credentials, query values or provider error text.</summary>
public sealed record InstagramRequestDiagnostics
{
    /// <summary>Gets the Graph endpoint without its query or fragment.</summary>
    public string? RequestUrl { get; init; }
    /// <summary>Gets the response status, or null before a response was received.</summary>
    public int? HttpStatus { get; init; }
    /// <summary>Gets the number of attempted transport requests, zero or one.</summary>
    public long Requests { get; init; }
    /// <summary>Gets response-body bytes read, including the byte detecting an oversized response.</summary>
    public long Bytes { get; init; }
    /// <summary>Gets the provider's requested delay, or a conservative fallback for a rate limit.</summary>
    public TimeSpan? RetryAfter { get; init; }
    /// <summary>Gets Meta's numeric error code, without its error body.</summary>
    public int? ProviderCode { get; init; }
}
