# WoW.Two.Sdk.Beta.Be.Integrations.GitHub

> Typed `HttpClient` over the GitHub REST API. Each call carries a `Bearer` token from `IAccessTokenService`, so repo visibility (public + private) matches that token.

## Register

```csharp
builder.Services.AddHttpContextAccessTokenProvider();   // or your own IAccessTokenService
builder.Services.AddGitHubIntegration(o =>
{
    o.UserAgent = "my-product";              // default wow-two-sdk
    o.ApiVersion = "2022-11-28";             // X-GitHub-Api-Version; none by default
    o.Timeout = TimeSpan.FromSeconds(10);    // default 100 s
    o.AllowAnonymousCalls = true;            // no token → call without Authorization (public repos only)
});
```

Configures a typed client → `IGitHubClient` (base `https://api.github.com/`, accept `application/vnd.github+json`). `GitHubIntegrationOptions` binds the host section `Integrations:GitHub` after the delegate. Without `AllowAnonymousCalls`, a missing token reports `Unauthorized` without calling GitHub. The client caches nothing; `IGitHubRepositoryReader` below keeps reads per commit.

## Read per commit

```csharp
builder.Services.AddHybridCaching();
builder.Services.AddGitHubRepositoryReader(o => o.HeadFor = TimeSpan.FromMinutes(5));   // Integrations:GitHub:Reader

public sealed class Manifests(IGitHubRepositoryReader reader)
{
    public async Task<byte[]?> ReadAsync(string repo, bool fresh, CancellationToken ct)
    {
        if (await reader.HeadAsync(repo, "main", fresh, ct) is not { } commit)
            return null;                                                       // branch unreadable
        var file = await reader.ReadAsync(repo, commit, "product.json", 64 * 1024, ct);
        return file.Outcome == FileRead.Found ? file.Content : null;
    }
}
```

| Method | Kept | Read again |
|---|---|---|
| `HeadAsync(repo, branch, fresh, ct)` | the head, readable or not, for `HeadFor` (default 5 min) | after `HeadFor`; `fresh` reads now and keeps a found head, a failed fresh read leaves the kept one |
| `ListAsync(repo, commit, purpose, keep, ct)` | the kept blob paths per commit and `purpose`, for `CommitFor` (default 1 day) | no tree (`NotFound` / `Unauthorized` / `Failed`) |
| `ReadAsync(repo, commit, path, maxBytes, ct)` | `Found` / `NotFound` / `TooLarge` per commit, path and limit | `Unauthorized` / `Failed` |

- Every consumer of a repository shares one cache and one rule; what a file means stays with the caller.
- `HeadFor = 0` reads the head on every call, as a test host that pushes commits wants; `CommitFor` must be positive.
- `GitHubRepositoryReader.HeadKey(repo, branch)` names the head entry, so a caller can evict it.
- Scoped, like the typed client; needs `AddGitHubIntegration` and `AddHybridCaching`.
- Tests: the multi-repository `FakeGitHubClient` (`Testing.Integrations`) under the real reader.

## Use

```csharp
public sealed class Probe(IGitHubClient gh)
{
    public async Task Run(CancellationToken ct)
    {
        RepoCheck repo  = await gh.RepoExistsAsync("owner/repo", ct);
        FileCheck file  = await gh.FileExistsAsync("owner/repo", ".github/workflows/ci.yml", ct);
        ReleaseList rel = await gh.GetLatestReleaseAsync("owner/repo", ct);
        ReleaseList rs  = await gh.GetReleasesAsync("owner/repo", limit: 10, ct);
        BuildRunCheck run = await gh.GetLatestWorkflowRunAsync("owner/repo", "ci.yml", ct);

        BranchHead head = await gh.GetBranchHeadAsync("owner/repo", "main", ct);
        RepositoryTree tree = await gh.GetTreeAsync("owner/repo", head.CommitSha!, ct);
        RepositoryFile file = await gh.ReadFileAsync("owner/repo", head.CommitSha!, "product.json", maxBytes: 64 * 1024, ct);
    }
}
```

## Surface

| Method | Returns | Notes |
|---|---|---|
| `RepoExistsAsync(repo, ct)` | `RepoCheck` | `Exists` / `NotFound` / `Unauthorized` / `Failed` |
| `FileExistsAsync(repo, path, ct)` | `FileCheck` | path is repo-relative; `Present` / `Absent` / … |
| `GetLatestReleaseAsync(repo, ct)` | `ReleaseList` | single latest release when `Found` |
| `GetReleasesAsync(repo, limit, ct)` | `ReleaseList` | newest first; `limit` clamped 1–100 |
| `GetLatestWorkflowRunAsync(repo, workflowFile, ct)` | `BuildRunCheck` | `workflowFile` e.g. `ci.yml`; `Succeeded` / `Failed` / `Running` / `None` / … |
| `GetBranchHeadAsync(repo, branch, ct)` | `BranchHead` | full commit SHA; `404/409/422 → NotFound` |
| `GetTreeAsync(repo, commitSha, ct)` | `RepositoryTree` | recursive; blob/tree/submodule entries with blob sizes; `IsTruncated` when GitHub cut it short |
| `ReadFileAsync(repo, commitSha, path, maxBytes, ct)` | `RepositoryFile` | raw bytes; `NotFound` / `TooLarge` (stops reading at the limit) / `Unauthorized` / `Failed` |

Each segment of a repository `path` and the `workflowFile` name is URL-escaped, so spaces, `#` and `?` stay part of the path.

`401/403 → Unauthorized`, `404 → NotFound`/`Absent`/`None`, transport + unexpected status → `Failed`/`ProbeFailed` (logged, never thrown).

## See also

- `IGitHubClient.cs` — interface + outcome types (`RepoCheck`, `FileCheck`, `ReleaseLookup`, `ReleaseInfo`, `ReleaseList`, `BuildRunCheck`, `RepositoryLookup`, `BranchHead`, `RepositoryTree`, `RepositoryTreeEntry`, `FileRead`, `RepositoryFile`).
- `IGitHubRepositoryReader.cs` · `GitHubRepositoryReader.cs` · `GitHubRepositoryReaderOptions.cs` — the commit-keyed reader.
- `../../Caching/Caching.md` — `ICacheRepository`, which keeps the reader's entries.
- `../integrations.md` — `IAccessTokenService` token source.
- [GitHub REST API](https://docs.github.com/en/rest).
