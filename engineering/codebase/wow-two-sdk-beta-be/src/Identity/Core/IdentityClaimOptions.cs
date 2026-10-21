using System.Security.Claims;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds the claim types the principal factory writes and the security-stamp check reads.</summary>
public sealed record IdentityClaimOptions
{
    /// <summary>Claim carrying the user id. Default <see cref="ClaimTypes.NameIdentifier"/>; JWT hosts often use <c>sub</c>.</summary>
    public string UserIdClaimType { get; set; } = ClaimTypes.NameIdentifier;

    /// <summary>Claim carrying the user name. Default <see cref="ClaimTypes.Name"/>.</summary>
    public string UserNameClaimType { get; set; } = ClaimTypes.Name;

    /// <summary>Claim carrying the email. Default <see cref="ClaimTypes.Email"/>.</summary>
    public string EmailClaimType { get; set; } = ClaimTypes.Email;

    /// <summary>Claim carrying each role. Default <see cref="ClaimTypes.Role"/>.</summary>
    public string RoleClaimType { get; set; } = ClaimTypes.Role;

    /// <summary>Claim carrying the security stamp. Default <c>AspNet.Identity.SecurityStamp</c>, as ASP.NET Identity writes it.</summary>
    public string SecurityStampClaimType { get; set; } = "AspNet.Identity.SecurityStamp";

    /// <summary>Claim carrying each organization role as <c>{organizationId}:{role}</c>. Default <c>org_role</c>.</summary>
    public string OrganizationRoleClaimType { get; set; } = OrganizationClaimTypeConstants.Role;

    /// <summary>Claim carrying each organization permission as <c>{organizationId}:{permission}</c>. Default <c>org_permission</c>.</summary>
    public string OrganizationPermissionClaimType { get; set; } = OrganizationClaimTypeConstants.Permission;
}
