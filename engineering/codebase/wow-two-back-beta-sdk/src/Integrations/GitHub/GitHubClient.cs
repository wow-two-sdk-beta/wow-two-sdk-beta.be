using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>Integrates with the GitHub REST API over a typed <see cref="HttpClient"/>, authorizing each call with the token from <see cref="IAccessTokenService"/> so repo visibility matches that token.</summary>
internal sealed partial class GitHubClient(
    HttpClient http,
    IAccessTokenService tokenProvider,
    GitHubIntegrationOptions options,
    ILogger<GitHubClient> logger) : IGitHubClient
{
    private const string RawMediaType = "application/vnd.github.raw";
    private const string ShaMediaType = "application/vnd.github.sha";
    private const int ReadChunkBytes = 16 * 1024;

    /// <inheritdoc />
    public async Task<RepoCheck> RepoExistsAsync(string repo, CancellationToken ct)
    {
        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return RepoCheck.Unauthorized;

        var response = await SendAsync(HttpMethod.Get, $"repos/{repo}", token, repo, ct);
        if (response is null)
            return RepoCheck.Failed;

        using (response)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.OK => RepoCheck.Exists,
                HttpStatusCode.NotFound => RepoCheck.NotFound,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => RepoCheck.Unauthorized,
                _ => Unexpected(repo, response.StatusCode, RepoCheck.Failed)
            };
        }
    }

    /// <inheritdoc />
    public async Task<FileCheck> FileExistsAsync(string repo, string path, CancellationToken ct)
    {
        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return FileCheck.Unauthorized;

        var response = await SendAsync(HttpMethod.Get, $"repos/{repo}/contents/{EscapePath(path)}", token, repo, ct);
        if (response is null)
            return FileCheck.Failed;

        using (response)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.OK => FileCheck.Present,
                HttpStatusCode.NotFound => FileCheck.Absent,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FileCheck.Unauthorized,
                _ => Unexpected(repo, response.StatusCode, FileCheck.Failed)
            };
        }
    }

    /// <inheritdoc />
    public async Task<ReleaseList> GetLatestReleaseAsync(string repo, CancellationToken ct)
    {
        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return ReleaseList.Empty(ReleaseLookup.Unauthorized);

        var response = await SendAsync(HttpMethod.Get, $"repos/{repo}/releases/latest", token, repo, ct);
        if (response is null)
            return ReleaseList.Empty(ReleaseLookup.Failed);

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var release = await ReadReleaseAsync(response, repo, ct);
                    return release is null
                        ? ReleaseList.Empty(ReleaseLookup.Failed)
                        : new ReleaseList { Outcome = ReleaseLookup.Found, Releases = [release] };
                case HttpStatusCode.NotFound:
                    return ReleaseList.Empty(ReleaseLookup.None);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return ReleaseList.Empty(ReleaseLookup.Unauthorized);
                default:
                    return ReleaseList.Empty(Unexpected(repo, response.StatusCode, ReleaseLookup.Failed));
            }
        }
    }

    /// <inheritdoc />
    public async Task<ReleaseList> GetReleasesAsync(string repo, int limit, CancellationToken ct)
    {
        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return ReleaseList.Empty(ReleaseLookup.Unauthorized);

        var perPage = Math.Clamp(limit, 1, 100);
        var response = await SendAsync(HttpMethod.Get, $"repos/{repo}/releases?per_page={perPage}", token, repo, ct);
        if (response is null)
            return ReleaseList.Empty(ReleaseLookup.Failed);

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var releases = await ReadReleasesAsync(response, repo, ct);
                    return releases is null
                        ? ReleaseList.Empty(ReleaseLookup.Failed)
                        : new ReleaseList { Outcome = releases.Count == 0 ? ReleaseLookup.None : ReleaseLookup.Found, Releases = releases };
                case HttpStatusCode.NotFound:
                    return ReleaseList.Empty(ReleaseLookup.None);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return ReleaseList.Empty(ReleaseLookup.Unauthorized);
                default:
                    return ReleaseList.Empty(Unexpected(repo, response.StatusCode, ReleaseLookup.Failed));
            }
        }
    }

    /// <inheritdoc />
    public async Task<BuildRunCheck> GetLatestWorkflowRunAsync(string repo, string workflowFile, CancellationToken ct)
    {
        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return BuildRunCheck.Unauthorized;

        var response = await SendAsync(
            HttpMethod.Get, $"repos/{repo}/actions/workflows/{Uri.EscapeDataString(workflowFile)}/runs?per_page=1", token, repo, ct);
        if (response is null)
            return BuildRunCheck.ProbeFailed;

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadRunAsync(response, repo, ct);
                case HttpStatusCode.NotFound:
                    return BuildRunCheck.None;
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return BuildRunCheck.Unauthorized;
                default:
                    return Unexpected(repo, response.StatusCode, BuildRunCheck.ProbeFailed);
            }
        }
    }

    /// <inheritdoc />
    public async Task<BranchHead> GetBranchHeadAsync(string repo, string branch, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return BranchHead.Missing(RepositoryLookup.Unauthorized);

        // The sha media type answers with the bare commit SHA instead of the full commit document.
        var response = await SendAsync(
            HttpMethod.Get, $"repos/{repo}/commits/{Uri.EscapeDataString(branch)}", token, repo, ct, ShaMediaType);
        if (response is null)
            return BranchHead.Missing(RepositoryLookup.Failed);

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadHeadAsync(response, repo, ct);
                case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                    // 409 = empty repository, 422 = no commit for that ref.
                    return BranchHead.Missing(RepositoryLookup.NotFound);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return BranchHead.Missing(RepositoryLookup.Unauthorized);
                default:
                    return BranchHead.Missing(Unexpected(repo, response.StatusCode, RepositoryLookup.Failed));
            }
        }
    }

    /// <inheritdoc />
    public async Task<RepositoryTree> GetTreeAsync(string repo, string commitSha, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return RepositoryTree.Empty(RepositoryLookup.Unauthorized);

        var response = await SendAsync(
            HttpMethod.Get, $"repos/{repo}/git/trees/{Uri.EscapeDataString(commitSha)}?recursive=1", token, repo, ct);
        if (response is null)
            return RepositoryTree.Empty(RepositoryLookup.Failed);

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadTreeAsync(response, repo, ct);
                case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                    return RepositoryTree.Empty(RepositoryLookup.NotFound);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return RepositoryTree.Empty(RepositoryLookup.Unauthorized);
                default:
                    return RepositoryTree.Empty(Unexpected(repo, response.StatusCode, RepositoryLookup.Failed));
            }
        }
    }

    /// <inheritdoc />
    public async Task<RepositoryFile> ReadFileAsync(string repo, string commitSha, string path, int maxBytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var (authorized, token) = await AuthorizeAsync(ct);
        if (!authorized)
            return RepositoryFile.Missing(FileRead.Unauthorized);

        // Headers-only completion streams the body, so an oversized file is abandoned at the limit instead of buffered.
        var response = await SendAsync(
            HttpMethod.Get,
            $"repos/{repo}/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(commitSha)}",
            token,
            repo,
            ct,
            RawMediaType,
            HttpCompletionOption.ResponseHeadersRead);
        if (response is null)
            return RepositoryFile.Missing(FileRead.Failed);

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadBoundedAsync(response.Content, maxBytes, repo, ct);
                case HttpStatusCode.NotFound:
                    return RepositoryFile.Missing(FileRead.NotFound);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return RepositoryFile.Missing(FileRead.Unauthorized);
                default:
                    return RepositoryFile.Missing(Unexpected(repo, response.StatusCode, FileRead.Failed));
            }
        }
    }

    /// <summary>Resolves the call's token; a missing token proceeds anonymously only when <see cref="GitHubIntegrationOptions.AllowAnonymousCalls"/> is set.</summary>
    private async Task<(bool Authorized, string? Token)> AuthorizeAsync(CancellationToken ct)
    {
        var token = await tokenProvider.GetAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
            return (options.AllowAnonymousCalls, null);

        return (true, token);
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string path,
        string? token,
        string repo,
        CancellationToken ct,
        string? accept = null,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // A per-call media type replaces the client default so GitHub answers in the shape the caller reads.
        if (accept is not null)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        try
        {
            return await http.SendAsync(request, completion, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogCallUnreachable(ex, method, path, repo);
            return null;
        }
    }

    private async Task<ReleaseInfo?> ReadReleaseAsync(HttpResponseMessage response, string repo, CancellationToken ct)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GitHubRelease>(ct);
            return ToReleaseInfo(payload);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogReleaseParseFailed(ex, repo);
            return null;
        }
    }

    private async Task<IReadOnlyList<ReleaseInfo>?> ReadReleasesAsync(HttpResponseMessage response, string repo, CancellationToken ct)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(ct);
            if (payload is null)
                return null;

            var releases = new List<ReleaseInfo>(payload.Count);
            foreach (var item in payload)
            {
                var info = ToReleaseInfo(item);
                if (info is not null)
                    releases.Add(info);
            }

            return releases;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogReleasesParseFailed(ex, repo);
            return null;
        }
    }

    private async Task<BuildRunCheck> ReadRunAsync(HttpResponseMessage response, string repo, CancellationToken ct)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GitHubWorkflowRuns>(ct);
            var run = payload?.Runs is { Count: > 0 } runs ? runs[0] : null;
            if (run is null)
                return BuildRunCheck.None;

            if (!string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
                return BuildRunCheck.Running;

            return string.Equals(run.Conclusion, "success", StringComparison.OrdinalIgnoreCase)
                ? BuildRunCheck.Succeeded
                : BuildRunCheck.Failed;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogWorkflowRunsParseFailed(ex, repo);
            return BuildRunCheck.ProbeFailed;
        }
    }

    private async Task<BranchHead> ReadHeadAsync(HttpResponseMessage response, string repo, CancellationToken ct)
    {
        try
        {
            var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (CommitShaPattern().IsMatch(text))
                return new BranchHead { Outcome = RepositoryLookup.Found, CommitSha = text };

            LogHeadParseFailed(repo);
            return BranchHead.Missing(RepositoryLookup.Failed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogCallUnreachable(ex, HttpMethod.Get, "commits", repo);
            return BranchHead.Missing(RepositoryLookup.Failed);
        }
    }

    private async Task<RepositoryTree> ReadTreeAsync(HttpResponseMessage response, string repo, CancellationToken ct)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GitHubTree>(ct);
            if (payload?.Entries is null)
            {
                LogTreeParseFailed(null, repo);
                return RepositoryTree.Empty(RepositoryLookup.Failed);
            }

            var entries = new List<RepositoryTreeEntry>(payload.Entries.Count);
            foreach (var item in payload.Entries)
            {
                var type = item.Type switch
                {
                    "blob" => RepositoryTreeEntryType.Blob,
                    "tree" => RepositoryTreeEntryType.Tree,
                    "commit" => RepositoryTreeEntryType.Commit,
                    _ => (RepositoryTreeEntryType?)null,
                };

                // An entry without a path or of an unknown kind carries nothing a caller can address.
                if (type is null || string.IsNullOrEmpty(item.Path))
                    continue;

                entries.Add(new RepositoryTreeEntry { Path = item.Path, Type = type.Value, Size = item.Size });
            }

            return new RepositoryTree { Outcome = RepositoryLookup.Found, Entries = entries, IsTruncated = payload.Truncated };
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogTreeParseFailed(ex, repo);
            return RepositoryTree.Empty(RepositoryLookup.Failed);
        }
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> of the body, stopping as soon as the limit is passed.</summary>
    private async Task<RepositoryFile> ReadBoundedAsync(HttpContent content, int maxBytes, string repo, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes)
            return RepositoryFile.Missing(FileRead.TooLarge);

        try
        {
            await using var stream = await content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[ReadChunkBytes];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes)
                    return RepositoryFile.Missing(FileRead.TooLarge);
            }

            return new RepositoryFile { Outcome = FileRead.Found, Content = buffer.ToArray() };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogCallUnreachable(ex, HttpMethod.Get, "contents", repo);
            return RepositoryFile.Missing(FileRead.Failed);
        }
    }

    private static ReleaseInfo? ToReleaseInfo(GitHubRelease? release)
    {
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return null;

        return new ReleaseInfo { Tag = release.TagName, PublishedAtUtc = release.PublishedAt };
    }

    /// <summary>Escapes each segment of a repository path so a space, <c>#</c> or <c>?</c> stays part of the path while the <c>/</c> separators survive.</summary>
    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private T Unexpected<T>(string repo, HttpStatusCode status, T failed)
    {
        LogUnexpectedStatus(repo, status);
        return failed;
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "GitHub call {Method} {Path} for {Repo} failed to reach the API.")]
    private partial void LogCallUnreachable(Exception exception, HttpMethod method, string path, string repo);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "GitHub release for {Repo} could not be parsed.")]
    private partial void LogReleaseParseFailed(Exception exception, string repo);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning, Message = "GitHub releases for {Repo} could not be parsed.")]
    private partial void LogReleasesParseFailed(Exception exception, string repo);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Warning, Message = "GitHub workflow runs for {Repo} could not be parsed.")]
    private partial void LogWorkflowRunsParseFailed(Exception exception, string repo);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Warning, Message = "GitHub call for {Repo} returned an unexpected status {Status}.")]
    private partial void LogUnexpectedStatus(string repo, HttpStatusCode status);

    [LoggerMessage(EventId = 5106, Level = LogLevel.Warning, Message = "GitHub branch head for {Repo} was not a commit SHA.")]
    private partial void LogHeadParseFailed(string repo);

    [LoggerMessage(EventId = 5107, Level = LogLevel.Warning, Message = "GitHub tree for {Repo} could not be parsed.")]
    private partial void LogTreeParseFailed(Exception? exception, string repo);

    /// <summary>A full SHA-1 or SHA-256 commit id, lowercase hex.</summary>
    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaPattern();

    /// <summary>The slice of a GitHub release payload a deployable version is keyed on.</summary>
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

    /// <summary>The slice of a GitHub workflow-runs response read to tell build status.</summary>
    private sealed record GitHubWorkflowRuns(
        [property: JsonPropertyName("workflow_runs")] List<GitHubWorkflowRun>? Runs);

    /// <summary>The status fields of a single GitHub Actions workflow run.</summary>
    private sealed record GitHubWorkflowRun(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion);

    /// <summary>The slice of a Git trees response a listing is built from.</summary>
    private sealed record GitHubTree(
        [property: JsonPropertyName("tree")] List<GitHubTreeItem>? Entries,
        [property: JsonPropertyName("truncated")] bool Truncated);

    /// <summary>One entry of a Git trees response.</summary>
    private sealed record GitHubTreeItem(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("size")] long? Size);
}
