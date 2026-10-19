# Settings files — standard

*Last updated: 2026-10-19*

> **Behavioral contract.** RFC 2119 (MUST / SHOULD / MAY). Changes only when the *contract* does, not when implementation moves.

## Purpose

Read a deployed target's mounted settings and a developer machine's own overrides under names that cannot be confused.
Source rule: `wow-two-ws` host configuration § *Settings files*.

## Files

- `appsettings.Target.json` **MUST** be read when present, on any run; a release bundle mounts it at `/app/`.
- `appsettings.Local.json` **MUST** be read only on a local run ([`LocalRun.standard.md`](LocalRun.standard.md)) whose
  configuration or process environment sets `LOCAL_SETTINGS` to `true`; a test host never sets it.
- Only a file present at startup **MUST** be added; neither **MUST** reload on change.
- A value set through the configuration indexer before the call **MUST** survive the insert, which rebuilds every
  provider.

## Order

- Both files **MUST** follow `appsettings.{Environment}.json`, the target's first, so user secrets, environment
  variables and every later source still win.
- Without `appsettings.{Environment}.json` they **MUST** follow `appsettings.json`; without either, they **MUST**
  precede the first environment-variable source, else come last.
- `AddSettingsFiles` **MUST** be idempotent; `AddApiDefaults` **MUST** call it before reading its options.

## Transition shim

- A deployed host (`DEPLOY_ENVIRONMENT` set) without `appsettings.Target.json` **MUST** read an existing
  `appsettings.Local.json` in the target's place: bundles older than pipelines v0.7.0 mount it there.
- It **MUST** log one warning at start that names both files.
- The shim **MUST** go in a later release once every product publishes on pipelines v0.7.0 or later.
