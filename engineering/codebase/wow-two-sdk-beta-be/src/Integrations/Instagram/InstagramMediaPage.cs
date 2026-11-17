using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Contains detached provider media records and an optional cursor from one bounded request.</summary>
public sealed record InstagramMediaPage
{
    /// <summary>Gets provider-shaped objects restricted to the requested media fields.</summary>
    public IReadOnlyList<JsonElement> Media { get; init; } = [];
    /// <summary>Gets the next cursor; an empty page always terminates enumeration.</summary>
    public string? After { get; init; }
    /// <summary>Gets sanitized request provenance and resource usage.</summary>
    public InstagramRequestDiagnostics Diagnostics { get; init; } = new();
}
