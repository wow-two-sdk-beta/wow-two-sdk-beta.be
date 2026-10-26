namespace WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness;

/// <summary>Represents the row an upsert left in the database, and what the upsert did to it.</summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed record UpsertOutcome<TEntity>
    where TEntity : class
{
    /// <summary>Gets the saved row, tracked by the context: the candidate when it was inserted, else the stored row.</summary>
    public required TEntity Entity { get; init; }

    /// <summary>Gets what the upsert did.</summary>
    public required UpsertAction Action { get; init; }
}
