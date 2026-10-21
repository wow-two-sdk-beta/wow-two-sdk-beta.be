namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Defines the seam reaching stored organizations and their memberships.</summary>
/// <typeparam name="TKey">User key type.</typeparam>
public interface IOrganizationRepository<TKey>
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>Stores a new organization together with its first owner, in one save.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="owner">The creator's membership.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(IdentityOrganization organization, IdentityMembership<TKey> owner, CancellationToken cancellationToken = default);

    /// <summary>Saves a changed organization.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(IdentityOrganization organization, CancellationToken cancellationToken = default);

    /// <summary>Deletes an organization and every membership in it.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>The organization with <paramref name="organizationId"/>, or null.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityOrganization?> FindByIdAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>The organization with the lowercase <paramref name="slug"/>, or null.</summary>
    /// <param name="slug">The handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityOrganization?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Stores a new membership.</summary>
    /// <param name="membership">The membership.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddMembershipAsync(IdentityMembership<TKey> membership, CancellationToken cancellationToken = default);

    /// <summary>The user's membership in the organization, or null.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityMembership<TKey>?> FindMembershipAsync(Guid organizationId, TKey userId, CancellationToken cancellationToken = default);

    /// <summary>Sets the member's role; false when the user is not a member.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="userId">The member.</param>
    /// <param name="role">The new role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ChangeRoleAsync(Guid organizationId, TKey userId, string role, CancellationToken cancellationToken = default);

    /// <summary>Deletes the membership; false when the user is not a member.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="userId">The member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> RemoveMembershipAsync(Guid organizationId, TKey userId, CancellationToken cancellationToken = default);

    /// <summary>The organization's memberships, earliest first.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityMembership<TKey>>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>The user's memberships with each organization's name and handle, earliest first.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OrganizationMembershipModel>> ListForUserAsync(TKey userId, CancellationToken cancellationToken = default);

    /// <summary>How many members of the organization hold <paramref name="role"/>.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="role">The role, as stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountInRoleAsync(Guid organizationId, string role, CancellationToken cancellationToken = default);
}
