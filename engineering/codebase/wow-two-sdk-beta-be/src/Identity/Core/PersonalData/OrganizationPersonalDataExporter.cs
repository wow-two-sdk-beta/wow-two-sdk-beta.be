using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>Exports the user's memberships as the <c>organizations</c> section; registered by <c>AddOrganizations</c>.</summary>
/// <typeparam name="TUser">The user entity.</typeparam>
/// <typeparam name="TKey">The primary-key type.</typeparam>
/// <param name="organizations">Reads the user's memberships.</param>
public sealed class OrganizationPersonalDataExporter<TUser, TKey>(OrganizationService<TUser, TKey> organizations) : IPersonalDataExporter<TUser>
    where TUser : IdentityUser<TKey>
    where TKey : notnull, IEquatable<TKey>
{
    /// <inheritdoc />
    public string Section => "organizations";

    /// <inheritdoc />
    public async Task<object?> ExportAsync(TUser user, CancellationToken cancellationToken = default)
        => (await organizations.ListForUserAsync(user, cancellationToken)).Select(m => new { m.Name, m.Slug, m.Role, m.JoinedAt }).ToList();
}
