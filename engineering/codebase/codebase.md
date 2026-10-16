# codebase

*Last updated: 2026-10-16*

One NuGet mono-lib, one code dir (per [`sdk-structure.md`](../../../../../conventions/development/repo/structure/sdk-structure.md)).

- [`wow-two-sdk-beta-be/`](./wow-two-sdk-beta-be/) — the `WoW.Two.Sdk.Beta.Be` package: `src/` holds every csproj, `WoW.Two.Sdk.Beta.Be.slnx`, `Directory.{Build,Packages}.props`, and `.editorconfig`.

| Run from | Command |
|---|---|
| `wow-two-sdk-beta-be/src` | `dotnet build WoW.Two.Sdk.Beta.Be.slnx -m:1` (`-m:1` + `MSBUILDDISABLENODEREUSE=1` + `ulimit -n 65535` avoid EMFILE on macOS) |
| repo root (CI) | `publish.yml` sets `defaults.run.working-directory: engineering/codebase/wow-two-sdk-beta-be` — every `src/…` path in the workflow is code-dir-relative |

Package conventions + layering rules: repo-root [`CLAUDE.md`](../../CLAUDE.md) · shape: [`../architecture/package-layout.md`](../architecture/package-layout.md).
