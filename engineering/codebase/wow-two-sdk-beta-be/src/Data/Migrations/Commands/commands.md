# WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands

*Last updated: 2026-10-28*

> The migrate verb tree, built once: an image answers it as `app migrate <verb>`, and `wow-migrate` compiles the same
> sources over a manifest. Web-free — `System.CommandLine` only — so the tool links it without ASP.NET.

## Host

```csharp
using WoW.Two.Sdk.Beta.Be.Data;

var app = builder.Build();
if (MigrateCommand.IsCommand(args))
    return await app.RunMigrateCommandAsync(args);
app.Configure();
```

- `MigrateCommand` and `RunMigrateCommandAsync` (an `IHost` extension) live in `Data/`; the tree here is internal.
- The helper runs before any startup work and never starts the host; it returns the verb's exit.
- `Migrations:ConnectionString`, when set, connects every bespoke set for the run: the helper turns `MigrationRunMode`
  on before any runner resolves, so the product calls no `AddMigrationConnectionOverride()`.
- Console output stays on stderr for the rest of the process, so the result line is the last stdout line.
- `MigrateCommand.RunAsync(services, args, output, error)` runs the same tree in process: the verb harness's seam.

## Verbs

| Verb | Engine call | Guard |
|---|---|---|
| bare `migrate` | `apply`, then `seed` once every set applied | — |
| `check [--set s] [--json]` | `CheckAsync` | — |
| `plan` · `locks` | `PlanAsync` · `LocksAsync` | — |
| `apply [--dry-run]` | creates each database when missing (never on a dry run), then `ApplyAsync` | — |
| `seed` | `SeedAsync` | — |
| `sets [--json]` | `DescribeAsync`: no settings, no database | — |
| `rollback --to N` · `forget --above N [--include-contracts]` | `RollbackAsync` · `ForgetAsync` | `--confirm <db>` |
| `repair` · `baseline [--to N]` · `claim --owner o --instance i` · `unlock` | one engine call each | `--confirm <db>` |
| `bg …` | refused, exit `1`, until background migrations ship | — |

- `--set` and `--json` are recursive. A guarded verb runs on one set: `--set`, or the host's only set.
- `--confirm` compares ordinally with the database the set connects to, read from its settings without connecting
  (`IMigrationRunnerService.ReadDatabaseName`); missing or wrong → exit `1` with a rerun hint. No prompt, no `--force`.
- The engine's operator calls carry no `AllowRollback` gate; the typed confirmation is the guard, so `AllowRollback`
  stays off in the app.

## Output

- Exits `0` done or current · `1` refused · `2` failed · `3` busy, for every verb and for a parse error.
- stdout: the JSON report with `--json` (`sets`: `{"sets":[…]}`), then exactly one `migrate-result {json}` line, last.
- stderr: the human report and the engine's logs. Every message is redacted of `Password=` pairs and URI credentials.
- An exception that escapes a verb is classified here: configuration (`InvalidOperationException`, options validation,
  a missing file) refuses, anything else fails.

## Strategy sets

- `IMigrationStrategySet` lets an EF context (`AddEfMigrationsRunner`) or a DbUp journal (`AddDbUpRunner`) answer
  `check`, `plan`, `apply`, `seed` and `sets`; every other verb refuses it with exit `1`.
- Bespoke sets run first, in dependency order, then each strategy set; `apply` and `seed` stop at the first failure.
- Their journals carry no kinds, so history the image lacks reads `ahead-incompatible`; `apply --dry-run` refuses.

## Files

- `MigrateCommandBuilder` — the tree; `MigrateCommandRunner` — the bodies; `MigrateSetCatalog` — bespoke and strategy
  sets as one list; `MigrateReportWriter` — streams and redaction; `MigrateResults` — refusals and the bare `migrate`.
- `IMigrateCommandHost` · `IMigrateCommandScope` — where the tree runs: an image's services
  (`Data/ServiceProviderMigrateCommandHost`) or the tool's provider per invocation (`cli/CliMigrateCommandHost`).

## See also

- `../Bespoke/bespoke.md` § Verb engine — the engine every verb calls.
- `../migrations.md` — the strategy index.
- `../../../Testing.Data/Migrations/Migrations.md` — the verb harness for an image's E2E tier.
