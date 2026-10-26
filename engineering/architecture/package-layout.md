# Package layout

*Last updated: 2026-10-26*

> SDK-internal architecture — the repo/package shape, layering rule, package-id grammar, and the three-layer doc strategy for this mono-lib.
> Code-style conventions (naming, documentation) are **centralized** in `wow-two-ws/conventions/` and apply to this repo too; only layout/registry live here.

## Repo structure

```
wow-two-sdk-beta.be/
├── README.md · CLAUDE.md · .github/workflows/   ← entry docs + CI (root-pinned)
└── engineering/
    ├── engineering.md
    ├── planning/                                ← backlog.md
    ├── research/                                ← per-vector deep-dives (research.md lead)
    ├── scripts/                                 ← sweep.sh convention checks
    ├── architecture/                            ← this folder
    │   ├── package-layout.md                    ← this file
    │   ├── package-registry.md                  ← per-area shipped-status lookup table
    │   ├── templates/                           ← reusable templates
    │   └── analysis/philosophy/                 ← ideas.md + targets.md
    └── codebase/
        ├── codebase.md
        └── wow-two-sdk-beta-be/                 ← the one code dir → src/ below
```

Shell shape follows [`sdk-structure.md`](../../../../../conventions/development/repo/structure/sdk-structure.md).

## Code dir — `engineering/codebase/wow-two-sdk-beta-be/`

Paths below are code-dir-relative; `src/` moved here verbatim, so every MSBuild path is unchanged.

```
src/
├── WoW.Two.Sdk.Beta.Be.slnx           ← solution at src/ root (.slnx — canonical)
├── WoW.Two.Sdk.Beta.Be.csproj         ← core lib (globs every shipping folder)
├── Abstractions/                      ← kernel WoW2.Sdk.Beta.Be.Abstractions — BCL-only, own csproj
│   ├── Foundation/Results/ · Foundation/Errors/ · Messaging/ · Identity/CurrentUser/   ← moved types, namespaces kept
│   └── abstractions.md
├── Properties/KernelTypeForwarders.cs ← core's [TypeForwardedTo] per type moved to the kernel
├── Directory.Build.props              ← shared MSBuild properties
├── Directory.Packages.props           ← centralized package versions
├── .editorconfig                      ← rules for entire src/
│
├── Meta/
│   └── WoW.Two.Sdk.Beta.Be/           ← meta-package — refs curated set
│
├── Foundation/                        ← P1 — leaf-level deps
│   ├── Time/         → WoW.Two.Sdk.Beta.Be.Foundation.Time
│   ├── Errors/       → WoW.Two.Sdk.Beta.Be.Foundation.Errors (exception mapping; value types in the kernel)
│   ├── Validation/   → WoW.Two.Sdk.Beta.Be.Foundation.Validation
│   └── Serialization/ → WoW.Two.Sdk.Beta.Be.Foundation.Serialization
│
├── Observability/                     ← P1
│   ├── Logging/
│   ├── Tracing/
│   ├── Metrics/
│   └── HealthChecks/
│
├── Web/                               ← P1
│   ├── Hosting/
│   ├── OpenApi/
│   ├── ProblemDetails/
│   ├── RateLimit/
│   ├── OutputCache/
│   ├── SecureHeaders/
│   └── Cors/
│
├── Mediator/                          ← P2
├── Data/                              ← P3
├── Caching/                           ← P3
├── Http/                              ← P3
├── Messaging/                         ← P4
├── Jobs/                              ← P4
├── Comms/                             ← P4
├── Tenancy/                           ← P5
├── Ai/                                ← P5
├── FeatureFlags/                      ← P5
├── Realtime/                          ← P6
├── Storage/                           ← P6
├── Search/                            ← P6
├── Workflow/                          ← P6
│
└── Testing/                           ← P0 — parallel, ships continuously
    ├── Assertions/
    ├── Containers/
    ├── Verify/
    ├── Bogus/
    └── WireMock/

apps/                                  ← (planned) sandbox sibling of src/
└── playground/                        ← Aspire AppHost end-to-end demo
```

## Per-package folder shape

```
src/<area>/<package>/
├── WoW.Two.Sdk.Beta.Be.<Domain>.csproj
├── <Module>Module.cs                      ← `Add<Module>` IServiceCollection extension
├── <Public types>.cs                      ← API surface
├── Internal/                              ← anything `internal class`
├── <Module>.standard.md                   ← RFC 2119 behavioral contract
├── <Module>.spec.md                       ← API + usage snippets
├── <Module>.tests.cs                      ← runnable examples (xUnit)
└── <Folder>.md                            ← folder lead doc (PascalCase folder name, e.g. `Time.md`) — 1-screen quickstart. NOT `README.md` (only the repo root may be `README.md`)
```

> No `tests/` separate folder. Test files live next to the wrapper they document.
> Folder lead doc is `{Folder}.md` (PascalCase, matching the folder), never a per-folder `README.md` — see `repo-structure.md` §3.

## Package-id grammar

The package-id / folder / namespace shape is SDK-architecture and lives here. **Symbol-level naming** (extension-class / registration-method / options / predicates / data-verbs / acronyms / banned symbols) is centralized in `wow-two-ws/conventions/development/backend/code-style/naming.md` and applies to this repo.

- **NuGet package id**: `WoW2.Sdk.Beta.Be[.<Role>]` — the eight IDs are listed in the [package registry](package-registry.md#published-package-family)
- **Folder name**: `PascalCase`, matching the package-id / namespace segment 1:1 (`Time/`, `OutputCache/`, `FeatureFlags/`). Provider leaves nest under their concept parent — `Comms/Email/MailKit/` → `…Comms.Email.MailKit`, not a flat `Comms/EmailMailKit/`
- **Namespace**: follows the folder tree under `WoW.Two.Sdk.Beta.Be`, file-scoped (`namespace WoW.Two.Sdk.Beta.Be.Time;`)
- **Linux CI is case-sensitive** — build-file path globs (`DefaultItemExcludes`, `.slnx` paths, workflow `dotnet pack` paths) must match the on-disk PascalCase exactly; a lowercased path silently no-ops on `ubuntu-latest`

Examples: `WoW.Two.Sdk.Beta.Be` (meta) · `…Be.Caching.Redis` · `…Be.Identity.OAuth.Google` · `…Be.Testing.Containers.Postgres`.

## Layering rule

**Foundation can not import domain packages.** Foundation = `time`, `errors`, `results`, `validation`, `serialization`. Domain = everything else.

Domain packages can reference any sibling domain. Convention: keep dependencies one-way where natural; cycles never.

**Kernel.** `WoW2.Sdk.Beta.Be.Abstractions` (`src/Abstractions/`) sits below core and references nothing — no
`FrameworkReference`, no `PackageReference`. Core references it and never a package from a domain repo; a domain's
`{Domain}.Abstractions` contract references the kernel only. A type enters the kernel only when it is BCL-only and
core's own code reads it or the contracts of two repos use it; it keeps its namespace and core forwards it
(`src/Properties/KernelTypeForwarders.cs`). Rule: `wow-two-ws/conventions/development/repo/structure/domain-structure.md` § Kernel.

This mirrors the UI lib's foundation/domain rule. Future: enforce via custom Roslyn analyzer.

## Per-package file conventions

- Use **file-scoped namespaces** (`namespace WoW.Two.Sdk.Beta.Be.Time;`)
- Use **C# 13+** features freely (primary constructors, collection expressions, partial properties)
- **Nullable enabled** + warnings as errors for nullability
- **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`** on every csproj
- **Every public type has XML doc comment** (enforced via analyzer)
- **No `internal sealed class _<Helper>`** style — prefer `internal sealed class XxxHelper`

## Solution organization

Single `WoW.Two.Sdk.Beta.Be.slnx` at `src/` root, split into `Libraries` and `Tests` solution folders.

## Versioning

- All packages ship as `10.y.z-beta`.
- Single version across all eight packages — bumped together by CI on push to `main`; the kernel versions in lockstep with core.
- Consumers should pin exact versions for stability

## Doc strategy (three layers)

We wrap the .NET ecosystem (~10K+ packages reachable via composition). We can't maintain samples for every underlying lib — they change weekly. We maintain docs for **our wrappers**, whose cadence is our cadence. (The wrapper-doc *format* — `*.standard.md` / `*.spec.md` / xUnit-as-docs — is in `wow-two-ws/conventions/.../code-style/documentation.md`; the *strategy* is here.)

- **Layer 1 — wrapper code (always)**: each wrapper folder ships its `<Module>.standard.md` (RFC 2119 contract), `<Module>.spec.md` (API + usage), `<Module>.tests.cs` (runnable xUnit examples — the "Storybook for backend"), and the `{folder}.md` lead doc (1-screen quickstart + see-also)
- **Layer 2 — playground (one per repo)**: `apps/playground/` — a single .NET Aspire AppHost booting the meta-package end-to-end (DB, cache, messaging, identity, OTel dashboard). For onboarding, pre-release smoke, and cross-package debugging. Updated per major release, not per-wrapper
- **Layer 3 — underlying-lib reference (lazy)**: we do **not** maintain showcase content for EF Core / Polly / Serilog etc. — the wrapper's XML `<remarks>` and `spec.md` `## See also` link to canonical lib docs; subtle findings land **reactively** in the `wow-two-kb` org, never preemptively

**Cadence rule** — doc churn = *our* wrapper churn. If a wrapper doesn't change this month, its docs don't either; an underlying lib shipping 12 patch releases is irrelevant unless one breaks our wrapper (then bump the wrapper + update its spec). **Never write a sample app per underlying lib** — that's the 10K-packages trap.

## Build / pack

Eight release projects produce NuGets. Seven test projects evaluate `IsPackable=false`.
CI tests and verifies the release revision before publishing the family in lockstep.

Adding a published package (first done for the kernel, `10.0.77-beta`), all in one commit; paths from the code dir:

1. Create the project under `src/`, `PackageId` `WoW2.Sdk.Beta.Be.{Name}` (the reserved prefix), namespaces `WoW.Two.Sdk.Beta.Be.*`.
2. Add it to `src/WoW.Two.Sdk.Beta.Be.slnx` under `/Libraries/`.
3. Add its folder to the mono-lib's `DefaultItemExcludes` in on-disk casing; add a `ProjectReference` if core depends on it.
4. Add its `dotnet pack` line to `.github/workflows/publish.yml` (repo root).
5. Add the id to `scripts/verify-release-packages.sh`, `scripts/verify-published-packages.sh` and `scripts/test-verify-published-packages.py`.
6. Add its row to [package-registry.md](package-registry.md) and update the count above.
7. Check locally: a Release pack of every project, then `scripts/verify-release-packages.sh`.

- No NuGet step: trusted publishing creates a new id under the reserved prefix on the first push.
- Types moved out of core also get a `[TypeForwardedTo]` line in `src/Properties/KernelTypeForwarders.cs`.

## What does NOT live in src/

- Product-specific tests; SDK contract and integration tests live in the seven `*.Tests` projects.
- Sample apps for individual packages — only `apps/playground/` end-to-end
- Documentation generators — `engineering/` is hand-authored markdown
