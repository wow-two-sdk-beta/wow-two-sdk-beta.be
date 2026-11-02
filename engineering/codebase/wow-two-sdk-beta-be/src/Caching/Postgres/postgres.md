# Caching.Postgres

*A PostgreSQL table as the host's `IDistributedCache`, so sessions and cached entries survive restarts and deploys
without Redis.*

Namespace: `WoW.Two.Sdk.Beta.Be.Caching.Postgres`.

## Use

```csharp
// After any library that registers the memory default (AddIdentityBff calls AddDistributedMemoryCache) or before it:
// either order ends with the table as the IDistributedCache.
builder.Services.AddPostgresDistributedCache(o => o.ConnectionString = cfg.GetConnectionString("app")!);

// The host's migrate run applies the cache's set beside its own:
//   deploy → `app migrate`; local run → MigrateBespokeOnStartupAsync().
await app.Services.MigrateBespokeOnStartupAsync(app.Lifetime.ApplicationStopping);
```

```json
{ "Caching": { "Postgres": { "ConnectionString": "Host=db;Database=app;…", "Schema": "cache" } } }
```

| Setting (`Caching:Postgres`, applied last) | Default | Rule |
|---|---|---|
| `ConnectionString` | — | required; validated when the host starts |
| `Schema` | `cache` | lowercase identifier; the set claims it for one owner instance |
| `TableName` | `distributed_cache` | lowercase identifier; fixed once applied |
| `DefaultSlidingExpiration` | 20 min | positive; used when an entry names no expiration |
| `ExpiredItemsDeletionInterval` | 30 min | positive; the sweep's interval |

## Semantics

- Matches `Microsoft.Extensions.Caching.SqlServer`: absolute, relative and sliding expiration; a relative absolute wins
  over a fixed one; a fixed one in the past throws; the sliding window never passes the absolute one.
- A read never returns an expired row. A read or `Refresh` of a sliding entry moves its expiry in the same statement.
- `Set` upserts (`ON CONFLICT … DO UPDATE`), so hosts writing one key at once never fail; the last commit wins.
- Every value is a parameter; schema and table are validated identifiers, quoted.
- Expiry reads the registered `TimeProvider`, so tests drive it with `FakeTimeProvider`.
- Keys hold at most 2,048 UTF-8 bytes; values are `bytea`.
- HybridCache uses it as L2 automatically, like the Redis backend.

## Migration set `distributed_cache`

- Embedded in the SDK under `Caching/Postgres/Migrations/`; `001-distributed-cache` creates the table and its
  `expires_at` index. The cache never creates its table.
- Owns `Schema` as owner kind `module`: history in `{schema}.distributed_cache_migration_history`, its own advisory
  lock, the owner marker. Two applications sharing one database set different schemas.
- Connects through `ConnectionString` (a data source keyed `distributed_cache`), unless a migrate run's key connects
  every set. Its `Release` is the SDK's version.
- `app migrate sets` describes it without a connection string; only the cache and an apply need one.
- A renamed table is rewritten into the scripts in process, so only the default name applies through `wow-migrate`
  reading the SDK assembly; the image's own `app migrate` applies any name.

## Sweep

- `AddPeriodicJob` job `distributed_cache_sweep`: a pass at start, then every `ExpiredItemsDeletionInterval`.
- Deletes expired rows in batches of 1,000 with `FOR UPDATE SKIP LOCKED`: every host sweeps at once without waiting,
  and a row refreshed meanwhile survives. `DeleteExpiredItemsAsync` runs one sweep on demand.
- A failed pass, such as before the set is applied, is logged; the next pass runs on schedule.

## See also

- [Caching](../Caching.md) — the HybridCache facade this backs as L2.
- [Bespoke migrations](../../Data/Migrations/Bespoke/bespoke.md) — named sets, schema owners, the migrate tree.
- [Periodic jobs](../../Jobs/Core/core.md) — the sweep's host loop.
- Underlying lib: [Npgsql](https://www.npgsql.org/doc/) · [SQL Server cache semantics](https://learn.microsoft.com/aspnet/core/performance/caching/distributed#distributed-sql-server-cache).
