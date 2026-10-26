# Wheelhouse extractions — v0.6 lane A

*Last updated: 2026-10-26*

> Four generic backend blocks Wheelhouse built inline, extracted for every product (Wheelhouse v0.6 Adoption version,
> owner 2026-10-26). Rows refer to Wheelhouse's inventory, `engineering/research/sdk-extraction-v0.6.md` in
> `wow-two-platform.wheelhouse`.
> Each block ships SDK code, tests and its area doc; Wheelhouse adopts in a later lane, then the listed consumers.

## Status

- [x] W1 — periodic scoped jobs (row 1): `Jobs/Core`; 12 host tests on `FakeTimeProvider`
- [x] W2 — bounded child processes (row 2): `Integrations/Processes`; 23 tests over real `/bin/sh` children
- [x] W4 — atomic file writes and race-safe upserts (row 4): `Storage/FileSystem/Extensions`, `Data/…/Uniqueness`;
  14 file tests, 7 upsert tests on SQLite and PostgreSQL; `LocalFileBlobRepository` saves atomically
- [x] W5 — MCP request guard and tool errors (row 5): `Ai/Mcp`, on by default in `AddStatelessMcpServer` and
  `MapAuthenticatedMcp`; 30 tests

Verdicts: **ship** (in this pass) · **defer** (named trigger) · **skip** (reason).

---

## W1 — Periodic scoped jobs

Source: `VitalsSampler`, `DeploymentOutcomeFollower`, `DeploymentAutomationWorker`, `FleetRolloutWorker`,
`SettingsApplyWorker`, `InfraJobFollower` — six near-copies of one loop. Consumers: Haven, Ocharo Marketing, Config
Checker, Podcast Readiness, File Watch, Procedure Review, Matinee, TranscriptForge.

Surface: `AddPeriodicJob<TService>(name, (service, ct) => …, configure)` · `AddPeriodicJob(name, (services, ct) => …)` ·
`PeriodicJobOptions` · `PeriodicJobCadence` · `PeriodicJobBackgroundService`.

| Capability | Verdict |
|---|---|
| Interval, zero or less = off, read at host start (late configuration counts) | ship |
| Fresh DI scope per pass; service resolved per pass | ship |
| Log and continue; type-only logging for input-bearing exceptions | ship |
| Shutdown cancels the pass quietly; any other cancellation is a failed pass, not a dead loop | ship |
| Fixed delay and fixed rate cadences; initial delay | ship |
| Per-pass timeout | ship |
| `TimeProvider` delays; pass and duration metrics | ship |
| Duplicate job names refused at composition | ship |
| One runner across replicas (lease or advisory lock) | defer — first product running two replicas; `Data` transaction locks are the base |
| Last-success health check per job | defer — first product alerting on a stalled loop (Logs or Monitoring app) |
| Jitter and backoff after failures | defer — a job that hammers a failing dependency |
| Cron schedules | skip — Hangfire recurring jobs own calendars and persistence |

## W2 — Bounded child processes

Source: `DeploymentGateway`, `InfraGateway`, `RunnerFailureParser`. Consumers: TranscriptForge `CommandLineClient`
(yt-dlp, ffmpeg, no environment scrub); Matinee and Sift once they shell out.

Surface: `IProcessClient` / `ProcessClient` (`RunAsync`, `RunJsonAsync`, `Start`) · `ProcessSpec` · `ProcessOutput` ·
`ProcessClientOptions` · `ProcessEnvironmentConstants.Baseline` · `IProcessFailureParser` · `JsonFailureLineParser` ·
`ProcessFailureKind` · `ProcessFailureMapper` · `AddProcessClient()` / `AddProcessClient(name, …)` (keyed).

| Capability | Verdict |
|---|---|
| Argument list, never a shell; secrets through standard input | ship |
| Environment allowlist: nothing ambient passes; the app adds names; spec values set or remove | ship |
| Standard input always redirected and closed | ship |
| Timeout and caller cancellation kill the process tree | ship |
| Capped standard output (overflow kills the tree); standard-error tail | ship |
| JSON standard output; success exit codes | ship |
| Pluggable failure parser; JSON failure-line parser with kind map and message format | ship |
| Failures as `Result` errors marked with a kind, never echoing input or output | ship |
| Detached start (`Start`) for jobs the console reaps later | ship |
| Run metrics and logs without arguments | ship |
| Line-by-line output streaming (progress) | defer — TranscriptForge download progress |
| Windows job objects for tree kill | defer — first Windows host; `Kill(entireProcessTree)` covers Linux and macOS |
| CliWrap adapter | skip — the BCL covers every capability above with no dependency in the core |

## W4 — Atomic file writes and race-safe upserts

Source: `InventorySnapshotExporter`, `GitHubRunnerTokens` (two write copies), `EfProductManifests` (an upsert that
swallows a lost insert race). Consumers: ListingShelf, TranscriptForge, Matinee, TNIS (writes); ForeverPin, Ocharo
Marketing (upserts); the SDK's own `LocalFileBlobRepository`, which left a partial blob on a crash.

Surface: `AtomicFileExtensions` (`WriteAsync`, `WriteAllBytesAsync`, `WriteAllTextAsync`, `WriteJsonAsync`,
`CopyToFileAsync`, `IsTemporaryFile`) · `AtomicFileWriteOptions` · `UpsertExtensions` (`UpsertAsync`,
`AddIfAbsentAsync`) · `UpsertOutcome<TEntity>` · `UpsertAction`.

| Capability | Verdict |
|---|---|
| Temporary file beside the target, one rename; cleanup on failure and cancellation | ship |
| Exact Unix mode from creation (past the umask); a replacement keeps the old mode; folder mode | ship |
| Flush to disk before the swap | ship |
| Bytes, text, JSON, stream copy | ship |
| Crash debris recognizable and skipped by blob listings | ship |
| `LocalFileBlobRepository` saves atomically | ship |
| Upsert and insert-if-absent that re-read the winner on a unique conflict | ship |
| Savepoint inside a transaction; isolated retry outside one; other pending changes kept | ship |
| Folder fsync after the rename | defer — a product whose file must survive power loss after the call returns |
| Sweep of stale temporary files | defer — first long-lived folder that collects crash debris |
| Native `INSERT … ON CONFLICT` / `MERGE` upserts | defer — a hot path where the read-then-write round trip shows |

## W5 — MCP request guard and tool errors

Source: `McpRequestGuard`, `McpToolError`, `McpToolErrorFilter`, `McpSettings`. Consumers: TranscriptForge (bare
`MapMcp`), Ocharo Desk (own origin check, no cap), planned Events.

Surface: `McpRequestGuardOptions` · `McpOriginExtensions` · `McpRequestGuardMiddleware` · `McpToolError` ·
`McpToolErrorFilter` (`Apply`, `ToToolResult`); wired by default into `AddStatelessMcpServer(guard)` and
`MapAuthenticatedMcp`.

| Capability | Verdict |
|---|---|
| Foreign `Origin` → 403; own origin, configured origins and no `Origin` pass | ship |
| Body cap (default 64 KiB) bounding chunked bodies too; `null` lifts it | ship |
| Malformed allowed origins fail the boot | ship |
| Guard on by default for every `MapAuthenticatedMcp` endpoint | ship |
| `AppError` → `isError` tool result with `code`, `message`, `retryable`; other exceptions → generic message | ship |
| Builder for a tool that returns its failure | ship |
| Guard ahead of authentication (403 before 401) | skip — authorization already refuses an unauthenticated caller; the screen runs before the transport reads |
| Per-tool rate limits | defer — first public MCP endpoint; the web rate limiter covers the route |

---

## Adoption notes for Wheelhouse

- Jobs: each worker becomes one `AddPeriodicJob`; keep the clamps and configuration keys in `configure`, set
  `LogExceptionDetails = false` for the runner-backed passes.
- Processes: the SDK passes no ambient variable, while Wheelhouse inherits the environment and removes a few today;
  list what `transport.py` and `infra.py` need (for example `HOME` for SSH) in `InheritedVariables`, keep the explicit
  `WHEELHOUSE_*` values in `ProcessSpec.Environment`. Word messages per `ProcessFailureMapper.KindOf`; the shared
  parser becomes `JsonFailureLineParser { Kinds = { ["Rejected"] = Conflict }, Format = … }`.
- Files: `AtomicFileExtensions.WriteJsonAsync` / `WriteAllTextAsync` with `UnixCreateMode` 0600 and
  `DirectoryUnixCreateMode` 0700; keep the in-process turn that orders exports.
- Upsert: `EfProductManifests.SaveAsync` becomes `UpsertAsync`; detach the returned entity to keep the row untracked.
- MCP: delete `McpRequestGuard`, `McpToolError`, `McpToolErrorFilter`, `McpSettings.Allows`; bind `Mcp:AllowedOrigins`
  through `AddStatelessMcpServer(guard => …)` before re-pinning, or the SDK guard refuses those origins.
