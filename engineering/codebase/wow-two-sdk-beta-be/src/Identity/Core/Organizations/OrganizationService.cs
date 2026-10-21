using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>
/// Provides organizations and memberships: create, rename and delete an organization, and add, re-role and remove
/// its members. The creator becomes owner, and an organization always keeps one. Removing a member or changing a role
/// rotates the member's security stamp, so principals carrying the old role end; a new membership appears from the
/// next sign-in.
/// </summary>
/// <typeparam name="TUser">The user entity.</typeparam>
/// <typeparam name="TKey">The primary-key type.</typeparam>
/// <param name="organizations">The organization store.</param>
/// <param name="accounts">The core account service, for stamp rotation.</param>
/// <param name="options">The roles and their permissions; <c>Identity:Organizations</c> reloads live.</param>
/// <param name="time">The clock creations and memberships are stamped with.</param>
public sealed partial class OrganizationService<TUser, TKey>(
    IOrganizationRepository<TKey> organizations,
    UserAccountService<TUser, TKey> accounts,
    IOptionsMonitor<OrganizationOptions> options,
    TimeProvider time)
    where TUser : IdentityUser<TKey>
    where TKey : notnull, IEquatable<TKey>
{
    private const int NameMaxLength = 120;
    private const int SlugMaxLength = 64;

    /// <summary>Create <paramref name="organization"/> with <paramref name="owner"/> as its first owner, in one save.</summary>
    /// <param name="organization">The organization; an empty id is assigned.</param>
    /// <param name="owner">The creator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> CreateAsync(IdentityOrganization organization, TUser owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(owner);
        var check = CheckName(organization.Name);
        if (!check.Succeeded)
            return check;
        check = await CheckSlugAsync(organization, organization.Slug, cancellationToken);
        if (!check.Succeeded)
            return check;

        var now = time.GetUtcNow();
        if (organization.Id == Guid.Empty)
            organization.Id = Guid.CreateVersion7(now);
        organization.Name = organization.Name.Trim();
        organization.Slug = NormalizeSlug(organization.Slug);
        organization.CreatedAt = now;
        var membership = new IdentityMembership<TKey> { OrganizationId = organization.Id, UserId = owner.Id, Role = options.CurrentValue.OwnerRole, CreatedAt = now };
        await organizations.CreateAsync(organization, membership, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Rename <paramref name="organization"/>.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RenameAsync(IdentityOrganization organization, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var check = CheckName(name);
        if (!check.Succeeded)
            return check;

        organization.Name = name.Trim();
        await organizations.UpdateAsync(organization, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Change the handle of <paramref name="organization"/>, keeping handles unique.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="slug">The new handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> ChangeSlugAsync(IdentityOrganization organization, string slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var check = await CheckSlugAsync(organization, slug, cancellationToken);
        if (!check.Succeeded)
            return check;

        organization.Slug = NormalizeSlug(slug);
        await organizations.UpdateAsync(organization, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Delete <paramref name="organization"/> and its memberships, rotating every member's stamp.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> DeleteAsync(IdentityOrganization organization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var members = await organizations.ListMembersAsync(organization.Id, cancellationToken);
        await organizations.DeleteAsync(organization.Id, cancellationToken);
        foreach (var member in members)
        {
            if (await accounts.FindByIdAsync(member.UserId, cancellationToken) is { } user)
                await accounts.RotateSecurityStampAsync(user, cancellationToken);
        }

        return IdentityResult.Success;
    }

    /// <summary>Find an organization by id, or null.</summary>
    /// <param name="organizationId">The organization id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IdentityOrganization?> FindByIdAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => organizations.FindByIdAsync(organizationId, cancellationToken);

    /// <summary>Find an organization by handle (case-insensitive), or null.</summary>
    /// <param name="slug">The handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IdentityOrganization?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        return organizations.FindBySlugAsync(NormalizeSlug(slug), cancellationToken);
    }

    /// <summary>Add <paramref name="user"/> to <paramref name="organization"/> with <paramref name="role"/>.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="user">The user.</param>
    /// <param name="role">A configured role name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> AddMemberAsync(IdentityOrganization organization, TUser user, string role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(user);
        if (ResolveRole(role) is not { } resolved)
            return UnknownRole(role);
        if (await organizations.FindMembershipAsync(organization.Id, user.Id, cancellationToken) is not null)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UserAlreadyMember, $"The user is already a member of '{organization.Name}'.", IdentityUserExtensions.Param("OrganizationName", organization.Name));

        await organizations.AddMembershipAsync(new IdentityMembership<TKey> { OrganizationId = organization.Id, UserId = user.Id, Role = resolved, CreatedAt = time.GetUtcNow() }, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Give the member <paramref name="role"/> and rotate their stamp; the last owner cannot be demoted.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="user">The member.</param>
    /// <param name="role">A configured role name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> ChangeRoleAsync(IdentityOrganization organization, TUser user, string role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(user);
        if (ResolveRole(role) is not { } resolved)
            return UnknownRole(role);
        if (await organizations.FindMembershipAsync(organization.Id, user.Id, cancellationToken) is not { } membership)
            return NotMember(organization);
        if (string.Equals(membership.Role, resolved, StringComparison.OrdinalIgnoreCase))
            return IdentityResult.Success;
        if (IsOwner(membership.Role) && !IsOwner(resolved) && await IsLastOwnerAsync(organization, cancellationToken))
            return LastOwner(organization);

        await organizations.ChangeRoleAsync(organization.Id, user.Id, resolved, cancellationToken);
        return await accounts.RotateSecurityStampAsync(user, cancellationToken);
    }

    /// <summary>Remove the member and rotate their stamp; the last owner cannot leave.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="user">The member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RemoveMemberAsync(IdentityOrganization organization, TUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(user);
        if (await organizations.FindMembershipAsync(organization.Id, user.Id, cancellationToken) is not { } membership)
            return NotMember(organization);
        if (IsOwner(membership.Role) && await IsLastOwnerAsync(organization, cancellationToken))
            return LastOwner(organization);

        await organizations.RemoveMembershipAsync(organization.Id, user.Id, cancellationToken);
        return await accounts.RotateSecurityStampAsync(user, cancellationToken);
    }

    /// <summary>The user's role in <paramref name="organization"/>, or null when not a member.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string?> GetRoleAsync(IdentityOrganization organization, TUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(user);
        return (await organizations.FindMembershipAsync(organization.Id, user.Id, cancellationToken))?.Role;
    }

    /// <summary>The organization's memberships, earliest first.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<IdentityMembership<TKey>>> ListMembersAsync(IdentityOrganization organization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return organizations.ListMembersAsync(organization.Id, cancellationToken);
    }

    /// <summary>The user's memberships with each organization's name and handle, earliest first.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<OrganizationMembershipModel>> ListForUserAsync(TUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return organizations.ListForUserAsync(user.Id, cancellationToken);
    }

    /// <summary>The permissions <paramref name="role"/> grants; empty for an unknown role or one without permissions.</summary>
    /// <param name="role">The role name (case-insensitive).</param>
    public IReadOnlyList<string> GetPermissions(string role)
        => options.CurrentValue.Roles.TryGetValue(role, out var permissions) ? permissions : [];

    /// <summary>
    /// The user's organization claims: one role claim and one claim per permission for each membership, valued
    /// <c>{organizationId}:{name}</c>.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="claimTypes">The claim types to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Claim>> GetClaimsAsync(TUser user, IdentityClaimOptions claimTypes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claimTypes);
        var claims = new List<Claim>();
        foreach (var membership in await organizations.ListForUserAsync(user.Id, cancellationToken))
        {
            var organizationId = membership.OrganizationId.ToString("D", CultureInfo.InvariantCulture);
            claims.Add(new Claim(claimTypes.OrganizationRoleClaimType, $"{organizationId}:{membership.Role}"));
            foreach (var permission in GetPermissions(membership.Role).Distinct(StringComparer.Ordinal))
                claims.Add(new Claim(claimTypes.OrganizationPermissionClaimType, $"{organizationId}:{permission}"));
        }

        return claims;
    }

    private static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

    private static IdentityResult CheckName(string? name)
        => string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength
            ? IdentityUserExtensions.Failure(IdentityErrorCodeConstants.OrganizationNameRequired, $"An organization needs a name of 1–{NameMaxLength} characters.", IdentityUserExtensions.Param("MaxLength", NameMaxLength))
            : IdentityResult.Success;

    private async Task<IdentityResult> CheckSlugAsync(IdentityOrganization organization, string? slug, CancellationToken cancellationToken)
    {
        var normalized = NormalizeSlug(slug ?? string.Empty);
        if (normalized.Length is 0 or > SlugMaxLength || !SlugPattern().IsMatch(normalized))
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvalidOrganizationSlug, $"'{slug}' is not a valid handle: use 1–{SlugMaxLength} lowercase letters, digits and single hyphens.", IdentityUserExtensions.Param("Slug", slug));
        if (await organizations.FindBySlugAsync(normalized, cancellationToken) is { } holder && holder.Id != organization.Id)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.DuplicateOrganizationSlug, $"Handle '{normalized}' is already taken.", IdentityUserExtensions.Param("Slug", normalized));

        return IdentityResult.Success;
    }

    private string? ResolveRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        var current = options.CurrentValue;
        if (string.Equals(role, current.OwnerRole, StringComparison.OrdinalIgnoreCase))
            return current.OwnerRole;
        return current.Roles.Keys.FirstOrDefault(name => string.Equals(name, role, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsOwner(string role) => string.Equals(role, options.CurrentValue.OwnerRole, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsLastOwnerAsync(IdentityOrganization organization, CancellationToken cancellationToken)
        => await organizations.CountInRoleAsync(organization.Id, options.CurrentValue.OwnerRole, cancellationToken) <= 1;

    private static IdentityResult UnknownRole(string? role)
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UnknownOrganizationRole, $"Role '{role}' is not configured for organizations.", IdentityUserExtensions.Param("Role", role));

    private static IdentityResult NotMember(IdentityOrganization organization)
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UserNotMember, $"The user is not a member of '{organization.Name}'.", IdentityUserExtensions.Param("OrganizationName", organization.Name));

    private static IdentityResult LastOwner(IdentityOrganization organization)
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.LastOrganizationOwner, $"'{organization.Name}' must keep at least one owner.", IdentityUserExtensions.Param("OrganizationName", organization.Name));

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
