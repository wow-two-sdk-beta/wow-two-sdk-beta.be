using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// An organization people belong to through memberships — a brand, a team or a customer account. Consumed by the
/// organizations slice; one person holds at most one membership in it.
/// </summary>
public class IdentityOrganization : IKeyedEntity<Guid>
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The display name, 1–120 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The unique lowercase kebab-case handle, such as <c>ocharo</c>; 1–64 characters.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>When it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
