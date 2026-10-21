namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>One of a user's memberships, with the organization's name and handle.</summary>
public sealed record OrganizationMembershipModel
{
    /// <summary>The organization.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>The organization's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The organization's handle.</summary>
    public required string Slug { get; init; }

    /// <summary>The user's role in it.</summary>
    public required string Role { get; init; }

    /// <summary>When the user joined (UTC).</summary>
    public required DateTimeOffset JoinedAt { get; init; }
}
