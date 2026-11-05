namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

/// <summary>Defines a read-only view of who is calling: a person, a guest, a service or an API key, and what it holds.</summary>
public interface ICurrentPrincipal
{
    /// <summary>Gets the <c>sub</c> value: a person id, client id or API key id, or <c>null</c> when anonymous.</summary>
    string? Subject { get; }

    /// <summary>Gets how this principal is identified.</summary>
    PrincipalKind Kind { get; }

    /// <summary>Gets the <c>iss</c> value, or <c>null</c> in process.</summary>
    string? Issuer { get; }

    /// <summary>Gets the client id of the calling app, or <c>null</c> when no app is named.</summary>
    string? ClientId { get; }

    /// <summary>Gets the <c>org_id</c> value: the one active organization, or <c>null</c> when none is active.</summary>
    string? OrganizationId { get; }

    /// <summary>Gets the roles this principal holds.</summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>Gets the permissions this principal holds.</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Gets the scopes this principal holds.</summary>
    IReadOnlySet<string> Scopes { get; }

    /// <summary>Gets the <c>act</c> value: who acts for the person, or <c>null</c> when the person acts directly.</summary>
    ActorInfo? Actor { get; }

    /// <summary>Gets the tenant a trusted issuer or API key stamped, or <c>null</c> when none was stamped.</summary>
    string? Partition { get; }
}
