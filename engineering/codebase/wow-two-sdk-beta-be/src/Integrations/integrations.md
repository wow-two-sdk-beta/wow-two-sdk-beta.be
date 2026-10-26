# WoW.Two.Sdk.Beta.Be.Integrations

> Typed clients for third-party services (GitHub REST, GHCR) and for child processes. The HTTP clients authorize their calls with a pluggable `IAccessTokenService` — the token source is yours to choose.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be
```

## Register

```csharp
builder.Services.AddHttpContextAccessTokenProvider();   // token source (signed-in user's access_token)
builder.Services.AddGitHubIntegration();                // IGitHubClient
builder.Services.AddGhcrIntegration();                  // IContainerRegistryClient
```

## `IAccessTokenService` — the parameterized token source

Both clients resolve their per-call Bearer/Basic token from this one contract:

```csharp
public interface IAccessTokenService
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);   // null → unauthorized
}
```

- **Default:** `HttpContextAccessTokenService` reads the `access_token` saved on the current request's auth ticket (`SaveTokens = true`); `null` when there's no context or token. Register via `AddHttpContextAccessTokenProvider()` (adds `IHttpContextAccessor`).
- **Custom:** register your own `IAccessTokenService` (a PAT from config, a service token, a machine identity) before the integration calls. Off-request callers (background jobs) need a non-HttpContext provider.

## Clients

| Client | Interface | Register | Auth |
|---|---|---|---|
| GitHub REST | `IGitHubClient` | `AddGitHubIntegration()` | per-call `Bearer` from the provider |
| GHCR | `IContainerRegistryClient` | `AddGhcrIntegration()` | anon pull token → `Basic`-authed pull token (private) |
| Child processes | `IProcessClient` | `AddProcessClient()` / `AddProcessClient(name, …)` | none: an argument list and an allowlisted environment |

Every method returns a categorized outcome enum (`RepoCheck`, `FileCheck`, `ImageCheck`, …) — resilient, never throws on transport/HTTP errors; failures are logged and folded into a `Failed`/`ProbeFailed` value.

## See also

- `GitHub/github.md` — GitHub REST client surface.
- `Ghcr/ghcr.md` — GHCR client + the two-token pull flow.
- `Processes/processes.md` — bounded child processes: timeout that kills the tree, scrubbed environment, JSON output,
  failure-line parsing. Returns `Result`s rather than outcome enums.
- Identity's OAuth providers (`wow-two-sdk-beta.identity`) — sign-in that saves the `access_token` the default provider reads.
