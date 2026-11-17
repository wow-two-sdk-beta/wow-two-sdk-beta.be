namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Identifies one Instagram Business Discovery page without carrying credentials.</summary>
public sealed record InstagramMediaRequest
{
    /// <summary>Gets the Page-linked professional account used to query Meta.</summary>
    public required string ProfessionalAccountId { get; init; }
    /// <summary>Gets the explicit Graph API version, including its v prefix.</summary>
    public required string ApiVersion { get; init; }
    /// <summary>Gets the professional publisher's username without an at-sign.</summary>
    public required string Username { get; init; }
    /// <summary>Gets the requested maximum media count, from one to fifty.</summary>
    public int PageSize { get; init; } = 25;
    /// <summary>Gets the opaque continuation from a previous successful page.</summary>
    public string? After { get; init; }
}
