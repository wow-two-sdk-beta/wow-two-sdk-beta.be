# Testing.Data — Migrations

Test helpers for the SDK bespoke SQL migrator. Reference `WoW2.Sdk.Beta.Be.Testing.Data` from a test project.

- `MigratorHarness` — the DI-graph-under-test: `CreatePostgres` / `CreateSqlite` (filesystem root or embedded assembly) + `Runner`, `Options`, `ReadHistoryAsync`, `HasTableAsync`, `HasIndexAsync`, `OpenConnectionAsync`.
  - several sets: `CreatePostgres` / `CreateSqlite` with a `MigratorSetsBuilder` callback — `AddDefault(root | assembly)` and `Add(name, root | assembly | broker factory, configure)`, in apply order; every set gets the harness's dialect.
  - per set: `ApplyAllAsync`, `RunnerFor(name)`, `OptionsFor(name)`, `ReadHistoryAsync(name)`, `HasTableAsync(schema, table)`, `Sets`, `Services` for the multi-set helpers.
- `MigratorPostgresFixture` — a Postgres container whose `ResetAsync` drops every user schema (a module set's included) and recreates `public`, so each test applies from scratch — the opposite of base `PostgresFixture` (Respawn, preserves history). Serves directly as an `ICollectionFixture<>`.
- `MigratorTestBase` — drop-per-test convenience: resets the shared fixture + owns a fresh `MigrationsWorkspace`.
- `MigrationsWorkspace` — throwaway on-disk `{root}/NNN-name/{Apply,Rollback}.sql` root.
- `MigrationHistoryRow` — projected `migration_history` row for assertions.
- `DisableBespokeMigrator()` — replaces the dialect + runner of every set registered before it, named sets included, with no-ops so a startup migrate hook touches nothing (unit tiers whose schema comes from EF `EnsureCreated`).
- `AssertIdempotentAsync` / `CheckIdempotencyAsync` — the idempotency mode ([below](#idempotency-mode-m59)).
- `Lint/MigrationLint` — expand-only, lock-risk and published-table rules ([below](#lint-m22-m79)).
- `Verbs/MigrateVerbHarness` — an image's migrate verbs in process ([below](#an-images-e2e-tier-the-verb-harness-m57)).

## A migrations tier

Every repository whose set the step runs keeps a migrations tier (M53). Three calls cover it, on a reset database:

```csharp
[Collection(MigratorCollection.Name)]
public sealed class AppMigrationsTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    [Fact]
    public async Task Scripts_ShouldBeIdempotent()
    {
        await using var migrator = MigratorHarness.CreatePostgres(Fixture.ConnectionString, typeof(AppDbContext).Assembly);
        await migrator.AssertIdempotentAsync();
    }

    [Fact]
    public async Task Scripts_ShouldPassTheLint()
    {
        await using var migrator = MigratorHarness.CreatePostgres(Fixture.ConnectionString, typeof(AppDbContext).Assembly);
        await MigrationLint.AssertCleanAsync(migrator.Services, lint => lint.ExpandOnlyAfter("default", 30));
    }
}
```

### Idempotency mode (M59)

- `CheckIdempotencyAsync(set)` / `AssertIdempotentAsync(set)` run every script directly, history bypassed:
  - Apply → Apply: each `Apply.sql` twice; the second run succeeds and leaves the database exactly as the first
  - Rollback → Rollback: each `Rollback.sql` twice, newest first, same rule
  - Apply → Rollback → Apply: every `Apply.sql` again; the schema matches the first pass (rows may differ: fresh ids)
- Runs a `-- @no-transaction` file statement by statement, any other in a transaction; substitutes `{{schema}}` and a
  registered set's `{{schema:set}}`, and creates the set's schema.
- Needs an empty database and leaves the set applied; the report names the first failed migration and step.

### Lint (M22, M79)

`MigrationLint.CheckAsync(services, configure)` reads the provider's sets — sources, headers, `DependsOn`,
`ReferenceableTables` — and never connects; `AssertCleanAsync` throws `MigrationLintException` listing every finding.

| Rule | Flags | Runs |
|---|---|---|
| `expand-only` | a drop, rename, `ALTER COLUMN … TYPE`, `SET NOT NULL`, `TRUNCATE` or `DELETE FROM` without `-- @contract since=` (or the legacy `-- contract:`) | above `ExpandOnlyAfter(set, n)` |
| `index-concurrently` | `CREATE INDEX` without `CONCURRENTLY` on a table the file did not create | above `LintAfter(set, n)`, else every file |
| `constraint-not-valid` | a foreign key or check added to an existing table without `NOT VALID` | as above |
| `alter-column-type` | `ALTER COLUMN … TYPE` on an existing table | as above |
| `volatile-default` | `ADD COLUMN … DEFAULT gen_random_uuid()` (`random`, `clock_timestamp`, `nextval`, …) on an existing table | as above |
| `published-tables` | `REFERENCES` into another set's schema, or an EF mapping onto it (`CheckModel`), naming a table or column it does not publish, or without `DependsOn` | as above |

- Dropping an index or constraint the same file re-creates (`drop index concurrently if exists x; create index concurrently x …`) is no contract.
- A dependency the provider does not register declares its tables: `lint.Publish("identity", "identity", tables)`.
- Suppress one rule for one file with a reason: `-- lint-allow: index-concurrently users is empty until v0.6`; a
  suppression without a reason, or naming an unknown rule, is itself a finding.

## An image's E2E tier: the verb harness (M57)

`Verbs/MigrateVerbHarness` runs `app migrate <verb>` in process over the image's own services against the test
database, never starting the host:

```csharp
var verbs = new MigrateVerbHarness(factory.Services);   // the host's provider over the test database
await verbs.AssertCheckWritesNothingAsync();           // no schema, table, row, marker or lock appears; exit 0 or 1
await verbs.AssertExitAsync(0);                         // the bare migrate: apply, then seed
await verbs.AssertRerunChangesNothingAsync("apply");    // a rerun exits 0, reports no change, writes nothing
var sets = await verbs.RunAsync("sets", "--json");      // sets.Result is the parsed migrate-result line
```

- Every run checks the result line: the last stdout line starts `migrate-result `, parses, and carries the exit.
- Snapshots compare schemas, relations, columns, every table's rows and advisory locks (PostgreSQL), or `sqlite_master`
  and rows (SQLite); pass `connect` when the host has no unkeyed `IDbConnectionFactory`.
- Failures throw `MigrateVerbAssertionException` with the run's output.

## A module's set beside the host's

```csharp
await using var migrator = MigratorHarness.CreatePostgres(fixture.ConnectionString, sets => sets
    .AddDefault(workspace.Root)
    .Add("identity", typeof(IdentityModule).Assembly, options =>
    {
        options.SchemaName = "identity";
        options.SchemaOwner = "module";
    }));

(await migrator.ApplyAllAsync()).ValueOrThrow();
(await migrator.ReadHistoryAsync("identity")).Should().NotBeEmpty();
```

## Which Postgres fixture

Migrator suite (apply-from-scratch / drift / rollback) → `MigratorPostgresFixture`. E2E suite → base `Testing`'s `PostgresFixture`.

## Example

```csharp
[CollectionDefinition(Name)]
public sealed class MigratorCollection : ICollectionFixture<MigratorPostgresFixture>
{
    public const string Name = "app-migrator";
}

[Collection(MigratorCollection.Name)]
public sealed class ApplyTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    [Fact]
    public async Task Apply_creates_table()
    {
        Workspace.Write("001-baseline", "create table t1(id int primary key);", "drop table t1;");
        await using var migrator = CreateMigrator();
        await migrator.Runner.ApplyPendingAsync("test");
        (await migrator.HasTableAsync("t1")).ShouldBeTrue();
    }
}
```
