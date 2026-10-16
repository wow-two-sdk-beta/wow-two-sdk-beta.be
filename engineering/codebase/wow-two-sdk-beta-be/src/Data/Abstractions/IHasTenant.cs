namespace WoW.Two.Sdk.Beta.Be.Data.Abstractions;

/// <summary>Defines an entity scoped to a tenant.</summary>
/// <typeparam name="TTenantId">The tenant-id type.</typeparam>
public interface IHasTenant<TTenantId> : IEntity
{
    /// <summary>Gets or sets the tenant scope of the entity.</summary>
    TTenantId TenantId { get; set; }
}
