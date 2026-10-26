using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness.Extensions;

/// <summary>Extends EF Core saves with upserts keyed by a unique constraint, which survive losing the insert race to a concurrent writer.</summary>
/// <remarks>
///   - the stored row is read first; a missing one is inserted, and a unique conflict on that insert reads the winner's row and
///     applies the change to it instead of failing
///   - inside a transaction the insert runs under a savepoint, so a conflict leaves the transaction usable; outside one,
///     the failed save rolls back alone
///   - the save writes every pending change in the context, as <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> does
///   - the database constraint stays authoritative: a conflict on another constraint, or a concurrency conflict, still throws
/// </remarks>
public static class UpsertExtensions
{
    private const int MaxAttempts = 3;

    /// <summary>Inserts <paramref name="candidate"/>, or applies <paramref name="update"/> to the row <paramref name="key"/> finds — including a row a concurrent writer inserted first.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="key">Finds the row by the unique key the candidate carries, such as <c>row =&gt; row.ProductId == productId</c>.</param>
    /// <param name="candidate">The row to insert when none has the key.</param>
    /// <param name="update">Copies the new values onto the stored row.</param>
    /// <param name="constraintName">The PostgreSQL unique constraint on the key; <see langword="null"/> accepts any unique one.</param>
    /// <param name="cancellationToken">Cancels the read and the save.</param>
    /// <returns>The saved, tracked row and whether it was inserted or updated.</returns>
    /// <exception cref="DbUpdateConcurrencyException">The stored row changed or vanished under a concurrency token.</exception>
    public static Task<UpsertOutcome<TEntity>> UpsertAsync<TEntity>(
        this DbContext context,
        Expression<Func<TEntity, bool>> key,
        TEntity candidate,
        Action<TEntity> update,
        string? constraintName = null,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(update);
        return SaveAsync(context, key, candidate, update, constraintName, cancellationToken);
    }

    /// <summary>Inserts <paramref name="candidate"/> unless a row has its key — including a row a concurrent writer inserted first, which is returned unchanged.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="key">Finds the row by the unique key the candidate carries.</param>
    /// <param name="candidate">The row to insert when none has the key.</param>
    /// <param name="constraintName">The PostgreSQL unique constraint on the key; <see langword="null"/> accepts any unique one.</param>
    /// <param name="cancellationToken">Cancels the read and the save.</param>
    /// <returns>The stored, tracked row and whether it was inserted or already there.</returns>
    public static Task<UpsertOutcome<TEntity>> AddIfAbsentAsync<TEntity>(
        this DbContext context,
        Expression<Func<TEntity, bool>> key,
        TEntity candidate,
        string? constraintName = null,
        CancellationToken cancellationToken = default)
        where TEntity : class =>
        SaveAsync(context, key, candidate, update: null, constraintName, cancellationToken);

    private static async Task<UpsertOutcome<TEntity>> SaveAsync<TEntity>(
        DbContext context,
        Expression<Func<TEntity, bool>> key,
        TEntity candidate,
        Action<TEntity>? update,
        string? constraintName,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(candidate);

        for (int attempt = 1; ; attempt++)
        {
            TEntity? stored = await context.Set<TEntity>().FirstOrDefaultAsync(key, cancellationToken).ConfigureAwait(false);
            if (stored is not null)
            {
                if (update is null)
                {
                    return new UpsertOutcome<TEntity> { Entity = stored, Action = UpsertAction.Unchanged };
                }

                update(stored);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new UpsertOutcome<TEntity> { Entity = stored, Action = UpsertAction.Updated };
            }

            context.Add(candidate);
            if (await TryInsertAsync(context, candidate, constraintName, cancellationToken).ConfigureAwait(false))
            {
                return new UpsertOutcome<TEntity> { Entity = candidate, Action = UpsertAction.Inserted };
            }

            // Another writer inserted the key between the read and the insert; the next read finds its row.
            context.Entry(candidate).State = EntityState.Detached;
            if (attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(
                    $"The {typeof(TEntity).Name} upsert lost {MaxAttempts} insert races in a row; the key is being inserted and deleted concurrently.");
            }
        }
    }

    private static async Task<bool> TryInsertAsync(
        DbContext context, object candidate, string? constraintName, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            return await context.TrySaveChangesUniqueAsync(constraintName, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (
            UniqueSaveExtensions.IsUniqueViolation(exception.InnerException, constraintName)
            && exception.Entries.Any(entry => ReferenceEquals(entry.Entity, candidate)))
        {
            return false;
        }
    }
}
