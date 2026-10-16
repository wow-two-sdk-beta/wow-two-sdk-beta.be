namespace WoW.Two.Sdk.Beta.Be.Tenancy.Core;

/// <summary>Defines behavior that resolves <see cref="TenantInfo"/> for a tenant id — the registry of known tenants.</summary>
public interface ITenantRepository
{
    /// <summary>Finds the tenant with the given id.</summary>
    /// <param name="tenantId">The tenant id to look up.</param>
    /// <param name="cancellationToken">Token to cancel the lookup.</param>
    /// <returns>The tenant metadata, or <see langword="null"/> when unknown.</returns>
    ValueTask<TenantInfo?> FindAsync(string tenantId, CancellationToken cancellationToken = default);
}
