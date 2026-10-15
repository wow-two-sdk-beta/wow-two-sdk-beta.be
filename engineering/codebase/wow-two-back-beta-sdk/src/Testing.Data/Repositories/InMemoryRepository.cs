using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace WoW.Two.Sdk.Backend.Beta.Testing.Data.Repositories;

/// <summary>Keeps entities in a list behind the SDK repository contract, for tests that need no database.</summary>
/// <remarks>
///   - entities are matched by <see cref="IKeyedEntity{TId}.Id"/>, never by reference
///   - <see cref="UpdateAsync"/> replaces the stored row with the same id and is a no-op when none is stored
///   - not thread-safe; seed and read <see cref="Rows"/> directly to arrange and assert
/// </remarks>
/// <typeparam name="TEntity">The stored entity.</typeparam>
/// <typeparam name="TId">The primary-key type.</typeparam>
public class InMemoryRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class, IKeyedEntity<TId>
    where TId : notnull, IEquatable<TId>
{
    /// <summary>Gets the stored entities, in insertion order.</summary>
    public List<TEntity> Rows { get; } = [];

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id.Equals(id)));

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TEntity>>([.. Rows]);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Exists(row => row.Id.Equals(id)));

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rows.Count);

    /// <inheritdoc />
    public Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        Rows.Add(entity);
        return Task.FromResult(entity);
    }

    /// <inheritdoc />
    public Task CreateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        Rows.AddRange(entities);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var index = Rows.FindIndex(row => row.Id.Equals(entity.Id));
        if (index >= 0)
            Rows[index] = entity;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        Rows.RemoveAll(row => row.Id.Equals(entity.Id));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.RemoveAll(row => row.Id.Equals(id)) > 0);
}
