namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Contains a classified Instagram failure and a fixed, secret-free explanation.</summary>
public sealed record InstagramFailure
{
    /// <summary>Gets the failure case the consumer maps into its own vocabulary.</summary>
    public required InstagramFailureKind Kind { get; init; }
    /// <summary>Gets an explanation that never repeats provider error text or credential values.</summary>
    public required string Message { get; init; }
    /// <summary>Gets sanitized request provenance and resource usage.</summary>
    public InstagramRequestDiagnostics Diagnostics { get; init; } = new();
}
