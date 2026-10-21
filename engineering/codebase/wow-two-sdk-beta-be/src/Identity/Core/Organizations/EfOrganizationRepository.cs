using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Accesses organizations and memberships through the context hosting the identity schema.</summary>
/// <typeparam name="TKey">User key type.</typeparam>
/// <param name="context">The context hosting the identity schema.</param>
public sealed class EfOrganizationRepository<TKey>(DbContext context) : IOrganizationRepository<TKey>
    where TKey : notnull, IEquatable<TKey>
{
    private DbSet<IdentityOrganization> Organizations => context.Set<IdentityOrganization>();

    private DbSet<IdentityMembership<TKey>> Memberships => context.Set<IdentityMembership<TKey>>();

    /// <inheritdoc />
    public async Task CreateAsync(IdentityOrganization organization, IdentityMembership<TKey> owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(owner);
        Organizations.Add(organization);
        Memberships.Add(owner);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(IdentityOrganization organization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        if (context.Entry(organization).State == EntityState.Detached)
            Organizations.Update(organization);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = context.Database.CurrentTransaction is null && context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await Memberships.Where(m => m.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await Organizations.Where(o => o.Id == organizationId).ExecuteDeleteAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IdentityOrganization?> FindByIdAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

    /// <inheritdoc />
    public Task<IdentityOrganization?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => Organizations.FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);

    /// <inheritdoc />
    public async Task AddMembershipAsync(IdentityMembership<TKey> membership, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        Memberships.Add(membership);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IdentityMembership<TKey>?> FindMembershipAsync(Guid organizationId, TKey userId, CancellationToken cancellationToken = default)
        => Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId.Equals(userId), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ChangeRoleAsync(Guid organizationId, TKey userId, string role, CancellationToken cancellationToken = default)
        => await Memberships.Where(m => m.OrganizationId == organizationId && m.UserId.Equals(userId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Role, role), cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<bool> RemoveMembershipAsync(Guid organizationId, TKey userId, CancellationToken cancellationToken = default)
        => await Memberships.Where(m => m.OrganizationId == organizationId && m.UserId.Equals(userId)).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityMembership<TKey>>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await Memberships.AsNoTracking().Where(m => m.OrganizationId == organizationId).OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationMembershipModel>> ListForUserAsync(TKey userId, CancellationToken cancellationToken = default)
        => await (from membership in Memberships.AsNoTracking()
                  join organization in Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
                  where membership.UserId.Equals(userId)
                  orderby membership.CreatedAt
                  select new OrganizationMembershipModel
                  {
                      OrganizationId = organization.Id,
                      Name = organization.Name,
                      Slug = organization.Slug,
                      Role = membership.Role,
                      JoinedAt = membership.CreatedAt,
                  }).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountInRoleAsync(Guid organizationId, string role, CancellationToken cancellationToken = default)
        => Memberships.CountAsync(m => m.OrganizationId == organizationId && m.Role == role, cancellationToken);
}
