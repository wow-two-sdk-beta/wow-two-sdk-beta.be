namespace WoW.Two.Sdk.Beta.Be.Data.Abstractions;

/// <summary>Defines an entity that supports soft-delete: a row is deleted once <see cref="DeletedAt"/> is set.</summary>
/// <remarks>Maps to <c>deleted_at timestamptz NULL</c>; unique indexes filter <c>WHERE deleted_at IS NULL</c>.</remarks>
public interface ISoftDeletable : IEntity
{
    /// <summary>Gets or sets when the entity was soft-deleted, or null while it is live.</summary>
    DateTimeOffset? DeletedAt { get; set; }
}
