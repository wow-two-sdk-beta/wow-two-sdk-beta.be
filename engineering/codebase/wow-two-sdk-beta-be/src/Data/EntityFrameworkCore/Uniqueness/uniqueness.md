# Unique saves

> Unique-constraint conflicts as results, inside a transaction that stays usable.

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);
string? slug = await db.SaveWithGeneratedKeyAsync(
    code,
    applyKey: (entity, key) => entity.Slug = key,
    nextKey: () => ids.Generate(7, IdAlphabetConstants.Alphanumeric),
    maxAttempts: 5,
    constraintName: "ix_codes_slug",
    ct);
if (slug is null)
    return AppError.Of(AppErrorType.Conflict, "A unique link could not be allocated. Try again.");
await transaction.CommitAsync(ct);
```

- `TrySaveChangesUniqueAsync` saves under a savepoint and returns `false` on the named unique constraint.
- A conflict rolls back to the savepoint, detaches the entities being added and leaves the transaction usable.
- Any other failure still throws, including a unique violation on a different constraint.
- `SaveWithGeneratedKeyAsync` bounds the retries, so a saturated key space ends in `null`, not a spin.
- The database constraint stays authoritative; no exists-then-insert probe runs.
- SQLite reports no constraint name: there, any unique or primary-key violation counts.

## Upserts

```csharp
// Insert the manifest, or update the product's row — also when another request inserted it first.
var saved = await db.UpsertAsync(
    row => row.ProductId == manifest.ProductId,
    manifest,
    stored => { stored.Commit = manifest.Commit; stored.Name = manifest.Name; },
    constraintName: "ix_product_manifests_product_id",
    ct);   // saved.Action: Inserted | Updated

// Insert unless present; a concurrent first writer's row comes back unchanged.
var claim = await db.AddIfAbsentAsync(row => row.Key == key, new ClaimEntity { Key = key, Owner = owner }, ct: ct);
```

- The row is read first; a missing one is inserted, and a unique conflict on that insert re-reads the winner's row and
  applies the update to it — no swallowed `DbUpdateException`, no lost write.
- Inside a transaction the insert runs under a savepoint, so the transaction stays usable; outside one, the failed save
  rolls back alone and the context's other pending changes save with the retry.
- The save writes every pending change, as `SaveChangesAsync` does; the returned entity stays tracked.
- Three lost races in a row (the key inserted and deleted concurrently) throw `InvalidOperationException`.
- Another constraint's conflict and a concurrency-token conflict still throw.
