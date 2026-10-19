# Local run — standard

*Last updated: 2026-10-19*

> **Behavioral contract.** RFC 2119 (MUST / SHOULD / MAY). Changes only when the *contract* does, not when implementation moves.

## Purpose

Tell the developer's machine from a deployed host. Deployed dev runs as .NET `Development`, so `IsDevelopment()` alone
never means a local run. Source rule: `wow-two-ws` host configuration § *Settings files*.

## Signal

- `IsLocalRun` **MUST** return `true` only when the environment is `Development` and `DEPLOY_ENVIRONMENT` is absent.
- `DEPLOY_ENVIRONMENT` **MUST** count as present when either the host configuration or the process environment carries a
  non-blank value, so removing the ambient environment sources never turns a deployed host local.
- A blank or whitespace value **MUST** count as absent.
- `LOCAL_SETTINGS` **MUST NOT** affect the result: it only adds `appsettings.Local.json` on a local run.
- Seams that resolve from DI **MUST** treat a provider without `IHostEnvironment` as not local.

## Defaults keyed on it

Each default below **MUST** key on a local run, never on `IsDevelopment()` alone; an explicit option still wins.

| Behaviour | Seam | Override |
|---|---|---|
| EF Core sensitive-data logging | `AddEntityFrameworkCore`, `AddPostgresPersistence` | `EnableSensitiveDataLogging`, `ConfigureDbContext` |
| EF Core detailed errors | `AddEntityFrameworkCore`, `AddPostgresPersistence` | `EnableDetailedErrors`, `ConfigureDbContext` |
| OpenAPI document endpoint | `UseApiDefaults` | `ExposeOpenApi` |
| Data Protection without a key directory | `AddPersistentDataProtection` | `KeyDirectory` |

- A product's own local-only convenience — an open allowlist, log-delivered sign-in codes, relaxed rate limits,
  polling instead of webhooks, SignalR detailed errors — **SHOULD** key on `IsLocalRun` too.

## Tests

- A test host in `Development` without `DEPLOY_ENVIRONMENT` **MUST** count as a local run.
- A test that simulates deployed dev **SHOULD** set `DEPLOY_ENVIRONMENT` through host configuration, not the process
  environment, so parallel tests stay isolated.
