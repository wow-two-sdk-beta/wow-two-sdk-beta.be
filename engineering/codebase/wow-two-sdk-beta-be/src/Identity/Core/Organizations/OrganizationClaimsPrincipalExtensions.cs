using System.Globalization;
using System.Security.Claims;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Reads the organization claims a principal carries.</summary>
public static class OrganizationClaimsPrincipalExtensions
{
    /// <summary>The principal's role in <paramref name="organizationId"/>, or null when it carries no membership there.</summary>
    /// <param name="principal">The principal.</param>
    /// <param name="organizationId">The organization.</param>
    /// <param name="claimType">The role claim type; the default matches <see cref="IdentityClaimOptions.OrganizationRoleClaimType"/>.</param>
    public static string? GetOrganizationRole(this ClaimsPrincipal principal, Guid organizationId, string claimType = OrganizationClaimTypeConstants.Role)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var prefix = Prefix(organizationId);
        return principal.FindAll(claimType).Select(c => c.Value).FirstOrDefault(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    }

    /// <summary>Whether the principal holds <paramref name="permission"/> in <paramref name="organizationId"/>.</summary>
    /// <param name="principal">The principal.</param>
    /// <param name="organizationId">The organization.</param>
    /// <param name="permission">The permission name, compared exactly.</param>
    /// <param name="claimType">The permission claim type; the default matches <see cref="IdentityClaimOptions.OrganizationPermissionClaimType"/>.</param>
    public static bool HasOrganizationPermission(this ClaimsPrincipal principal, Guid organizationId, string permission, string claimType = OrganizationClaimTypeConstants.Permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        var expected = Prefix(organizationId) + permission;
        return principal.FindAll(claimType).Any(c => string.Equals(c.Value, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static string Prefix(Guid organizationId) => organizationId.ToString("D", CultureInfo.InvariantCulture) + ":";
}
