# WoW.Two.Sdk.Beta.Be.Data.Migrations

*Last updated: 2026-10-28*

> Strategy index for the three database-migration runners shipped side-by-side in this package — `Ef/`, `DbUp/`, `Bespoke/` — and the one migrate verb tree (`Commands/`) every image answers as `app migrate <verb>`. This doc **routes**; engine internals live in each sub-folder's own lead doc. `Bespoke/` is the intended default for wow-two products.

---

## The three strategies

All three register a startup runner via a descriptive `Add*` extension (no `WowTwo` prefix per `naming.md`); they differ in **who owns the schema** and **what host shapes they support**.

| Strategy | Folder | Register | Schema source-of-truth | Status |
|---|---|---|---|---|
| **EF Core** | `Ef/` | `AddEfMigrationsRunner<TContext>` | C# `DbContext` + EF migration classes (code-first) | shipped |
| **DbUp** | `DbUp/` | `AddDbUpRunner` | Embedded `.sql` scripts (DbUp journal) | shipped (legacy option) |
| **Bespoke SQL** | `Bespoke/` | `AddDatabaseBespokeMigrations` | Raw `Migrations/NNN-name/Apply.sql` pairs | shipped |

---

## Comparison

| Axis | `Ef/` (`AddEfMigrationsRunner`) | `DbUp/` (`AddDbUpRunner`) | `Bespoke/` (`AddDatabaseBespokeMigrations`) |
|---|---|---|---|
| **Schema ownership** | C# code-first — `DbContext` model + EF-generated migrations own DDL | Hand-written embedded `.sql` scripts | Hand-written raw `Apply.sql`; EF (if present) is a **pure mapper** |
| **Host-agnostic** | No — coupled to `DbContext` + `Database.MigrateAsync` (startup `IHostedService` only) | Partial — `DbUpBackgroundService` (startup only); engine is DbUp-internal | **Yes** — one `IMigrationRunnerService` reused by CLI · HTTP endpoint · hosted-service |
| **Rollback** | EF down-migrations (rarely used) | None (roll-forward only) | `Rollback.sql` as a confirmed operator verb (`IMigrationEngine.RollbackAsync`); hosts stay gated by `MigrationOptions.AllowRollback`; prod rolls forward |
| **Multi-provider** | Any EF provider (PG/SQL Server/SQLite/…) | PG · SQL Server · MySQL via `DbUpProviderFactory` (SQLite needs custom factory) | Postgres + SQLite; Postgres owns a whole-loop advisory lock, SQLite requires one deployment applicant |
| **Drift / checksum** | EF model-snapshot diff | Script journal (raw-byte hash) | Normalized SHA-256 over `Apply.sql` — no CRLF/whitespace false drift |
| **Concurrency (multi-instance)** | EF internal | None | Postgres: bounded advisory lock in a reserved key class; SQLite: deployment-owned single applicant (`busy_timeout` is not a mutex) |
| **Migrate tree** | `check` · `plan` · `apply` · `seed` · `sets`; other verbs refuse | same as EF | every verb (`Commands/`), in the image and in `wow-migrate` (`new`/`promote` besides), one set or a `--sets` manifest |
| **Several schemas per host** | one context per schema | one journal | named sets: own history, lock, schema and owner-and-instance marker per module (`AddDatabaseBespokeMigrationSet`) |
| **When to pick** | Code-first shop already invested in EF migrations | Existing DbUp script library being carried forward | **Default for new wow-two products** — SQL-owned schema, host-agnostic, rebaseline/squash |

---

## Decision guide

```
New wow-two product, SQL-owned schema, want CLI + startup + host-agnostic?
    └─ Bespoke/  →  AddDatabaseBespokeMigrations   (the default)

Already committed to EF code-first; C# model is the schema truth?
    └─ Ef/   →  AddEfMigrationsRunner<TContext>

Carrying a legacy embedded-.sql DbUp script library forward?
    └─ DbUp/ →  AddDbUpRunner
```

- Greenfield → start at `Bespoke/`. It is host-agnostic and has normalized-checksum drift detection. Postgres is safe for competing runners; SQLite deployments must nominate one applicant.
- `Ef/` and `DbUp/` are kept for consumers already invested in those models; they are **not** the recommended path for new products.
- One product picks **one** runner — they are mutually exclusive (each owns the schema differently). Do not register two. Several `Bespoke/` sets — the product's plus each module's — are one runner.

### Provider note

`Bespoke/` exposes its coordination boundary through `MigrationCoordinationMode`. PostgreSQL serializes the complete migration loop in the database. SQLite supplies transient write waiting only, so the deployment must run one migration applicant.

`Bespoke/` applies consecutive transactional files in one transaction by default, so a failed run leaves none of that batch applied; `wow-migrate apply --dry-run` rehearses pending files and rolls back, as a pre-deploy gate. Details: `Bespoke/bespoke.md` § *Batch atomicity and dry run*.

---

## The migrate command

Every image that hosts a set answers `app migrate <verb>` from two lines in `Program.cs`, after `Build()` and before
`Configure`:

```csharp
using WoW.Two.Sdk.Beta.Be.Data;

var app = builder.Build();
if (MigrateCommand.IsCommand(args))
    return await app.RunMigrateCommandAsync(args);
app.Configure();
```

- A bare `migrate` runs `apply`, then `seed` — the command a rollout declares. Verbs, guards and output:
  `Commands/commands.md`.
- Exits `0` done or current · `1` refused · `2` failed · `3` busy; the last stdout line is one `migrate-result {json}`.
- `Migrations:ConnectionString`, when set, connects every set for the run; the product wires nothing for it.
- EF and DbUp sets answer `check`, `plan`, `apply`, `seed` and `sets` through their runners; other verbs refuse them.
- `wow-migrate` compiles the same tree over a `--sets` manifest or a `--sql-dir`, plus the authoring verbs `new` and
  `promote`.

---

## Registration at a glance

```csharp
// Ef/ — code-first; applies pending EF migrations at startup for TContext
services.AddEfMigrationsRunner<AppDbContext>();
// options: EfMigrationsOptions { Enabled, MaxConnectAttempts, ConnectRetryDelay }

// DbUp/ — embedded .sql scripts; pick the engine via DbUpProviderFactory
services.AddDbUpRunner(cs, o =>
{
    o.UpgradeEngineFactory = DbUpProviderFactory.Postgres;   // or .SqlServer / .MySql
    o.ScriptsAssembly      = typeof(Program).Assembly;
});

// Bespoke/ — embedded raw SQL for runtime hosts; the intended default
services.AddDatabaseBespokeMigrations(typeof(Program).Assembly, options =>
{
    options.Provider = DatabaseProvider.Postgres;
});

// Bespoke/ — a domain module's set beside the product's, in its own schema
services.AddDatabaseBespokeMigrationSet("identity", typeof(IdentityModule).Assembly, options =>
{
    options.SchemaName = "identity";
    options.SchemaOwner = "module";
});
```

- Production applies migrations in an explicit deploy step: the image's `app migrate` (above), or `wow-migrate apply`. `MigrateBespokeOnStartupAsync()` applies every set only on a local run and verifies levels read-only on any deployed host, unless `ApplyWhenDeployed` opts in. `MigrateBespokeForDeployAsync()` stays for hosts not yet on the command. Details: `Bespoke/bespoke.md` § *Who applies*.
- An existing database adopts a set without running its scripts through `app migrate baseline --to N --confirm <db>` (or `wow-migrate baseline`). Details: `Bespoke/bespoke.md` § *Baseline*.
- `IMigrationEngine` serves every verb of the migrate command tree — `check`, `plan`, `apply`, `seed`, `sets` (`DescribeAsync`), `rollback`, `forget`, `repair`, `baseline`, `claim`, `locks`, `unlock` — over the registered sets, each returning a `MigrateRunResult` with exits `0` done or current · `1` refused · `2` failed · `3` busy. Details: `Bespoke/bespoke.md` § *Verb engine*.
- A file marked `-- @contract since=vX.Y` is a contract, any other an expand; an older release boots and applies on newer history it survives. Details: `Bespoke/bespoke.md` § *Kinds and compatibility*.
- Locks are bounded (`lock_timeout` per script, batch retries, a bounded advisory wait) and taken in a reserved key class apart from app locks. Details: `Bespoke/bespoke.md` § *Lock safety*.

---

## Where things live

- `Commands/` — the migrate verb tree (`MigrateCommandBuilder`, `MigrateCommandRunner`, `IMigrationStrategySet`), web-free and compiled into `wow-migrate` too; the host entry `MigrateCommand` + `RunMigrateCommandAsync` lives in `Data/`.
- `Ef/` — `EfMigrationsServiceCollectionExtensions` (`AddEfMigrationsRunner<TContext>`), `EfMigrationsBackgroundService<TContext>` (calls `Database.MigrateAsync`), `EfMigrationStrategySet<TContext>` (the context's set in the tree, named by `EfMigrationsOptions.SetName`), `EfMigrationsOptions`.
- `DbUp/` — `DbUpServiceCollectionExtensions` (`AddDbUpRunner`), `DbUpBackgroundService`, `DbUpMigrationStrategySet` (the journal's set in the tree, named by `DbUpOptions.SetName`), `DbUpOptions`, `DbUpProviderFactory` (`Postgres`/`SqlServer`/`MySql`).
- `Bespoke/` — `IMigrationRunnerService`/`MigrationRunnerService`, `IMigrationEngine`/`MigrationEngine` (the verbs, `MigrateRunResult`), `MigrationDescriptor`, `AddDatabaseBespokeMigrations` + `AddDatabaseBespokeMigrationSet`, `IMigrationSetRegistry`, `MigrationOptions`, and `Migrations/NNN-name/{Apply,Rollback}.sql` beside `Seeds/NNN-name.json`.

---

## See also

- `./Ef/` — EF Core code-first runner (`AddEfMigrationsRunner<TContext>`, `Database.MigrateAsync`).
- `./DbUp/` — DbUp embedded-script runner (`AddDbUpRunner`, `DbUpProviderFactory`).
- `./Bespoke/bespoke.md` — bespoke raw-SQL migrator design and current guarantee record.
- `./Commands/commands.md` — the migrate verb tree, its host helper and output contract.
- `../../Testing.Data/Migrations/Migrations.md` — the migrations tier's lint, idempotency mode and verb harness.
- `../Dapper/` — `IDbConnectionFactory` the `Sql/` engine runs over (BCL-only extraction pending; see `Bespoke/bespoke.md` §0.1).
- `conventions/development/backend/persistence/database.md` — schema-first rule + EF waste-rule (for `Bespoke/`, `Migrations/*/Apply.sql` is the canonical schema EF maps over).
