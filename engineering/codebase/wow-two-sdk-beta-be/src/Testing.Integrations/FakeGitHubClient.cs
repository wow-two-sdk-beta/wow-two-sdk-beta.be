using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace WoW.Two.Sdk.Beta.Be.Testing.Integrations;

/// <summary>
/// Connects tests to configurable GitHub outcomes without a remote probe, so a test never
/// calls the real GitHub REST API (which would also need a signed-in user's OAuth token).
/// </summary>
/// <remarks>
///   - defaults to the happy path — repo exists, file present, no releases, no workflow runs, a branch head at
///     <see cref="DefaultCommitSha"/> with an empty tree
///   - flip the properties, or chain the <c>With…</c> helpers, for the failure branches
///   - each setting is one canned outcome for every call, never varying per repo; only
///     <see cref="RepositoryFiles"/> varies per path, and it drives both the tree listing and the file reads
/// </remarks>
public sealed class FakeGitHubClient : IGitHubClient
{
    /// <summary>The commit SHA the default <see cref="Head"/> reports.</summary>
    public const string DefaultCommitSha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>The canned outcome every <see cref="RepoExistsAsync"/> call returns. Defaults to <see cref="RepoCheck.Exists"/>.</summary>
    public RepoCheck Repo { get; set; } = RepoCheck.Exists;

    /// <summary>The canned outcome every <see cref="FileExistsAsync"/> call returns. Defaults to <see cref="FileCheck.Present"/>.</summary>
    public FileCheck File { get; set; } = FileCheck.Present;

    /// <summary>The releases the lookups return, newest first; drives both the latest and the list calls. Defaults to empty.</summary>
    public IReadOnlyList<ReleaseInfo> Releases { get; set; } = [];

    /// <summary>The outcome the lookups report when <see cref="Releases"/> is populated; override for the failure paths. Defaults to <see cref="ReleaseLookup.Found"/>.</summary>
    public ReleaseLookup ReleaseOutcome { get; set; } = ReleaseLookup.Found;

    /// <summary>The canned outcome every <see cref="GetLatestWorkflowRunAsync"/> call returns. Defaults to <see cref="BuildRunCheck.None"/>.</summary>
    public BuildRunCheck WorkflowRun { get; set; } = BuildRunCheck.None;

    /// <summary>The canned outcome every <see cref="GetBranchHeadAsync"/> call returns. Defaults to found at <see cref="DefaultCommitSha"/>.</summary>
    public BranchHead Head { get; set; } = new() { Outcome = RepositoryLookup.Found, CommitSha = DefaultCommitSha };

    /// <summary>The outcome every <see cref="GetTreeAsync"/> call reports; when <see cref="RepositoryLookup.Found"/> the tree lists <see cref="RepositoryFiles"/> as blobs. Defaults to <see cref="RepositoryLookup.Found"/>.</summary>
    public RepositoryLookup TreeOutcome { get; set; } = RepositoryLookup.Found;

    /// <summary>The repository files by repo-relative path, at every commit. A read of a path not listed reports <see cref="FileRead.NotFound"/>. Defaults to empty.</summary>
    public IDictionary<string, byte[]> RepositoryFiles { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>When set, the outcome every <see cref="ReadFileAsync"/> call reports regardless of <see cref="RepositoryFiles"/>; a forced <see cref="FileRead.Found"/> still needs the path listed. Defaults to <see langword="null"/>.</summary>
    public FileRead? FileReadOutcome { get; set; }

    /// <summary>Sets the canned <see cref="RepoExistsAsync"/> outcome and returns this instance for chaining.</summary>
    /// <param name="result">The repo-existence outcome to return for every call.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithRepo(RepoCheck result)
    {
        Repo = result;
        return this;
    }

    /// <summary>Sets the canned <see cref="FileExistsAsync"/> outcome and returns this instance for chaining.</summary>
    /// <param name="result">The file-existence outcome to return for every call.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithFile(FileCheck result)
    {
        File = result;
        return this;
    }

    /// <summary>Sets the releases (newest first) the lookups return, forcing <see cref="ReleaseOutcome"/> to <see cref="ReleaseLookup.Found"/>, and returns this instance for chaining.</summary>
    /// <param name="releases">The releases to return, newest first.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithReleases(params ReleaseInfo[] releases)
    {
        Releases = releases;
        ReleaseOutcome = ReleaseLookup.Found;
        return this;
    }

    /// <summary>Forces both release lookups to report <paramref name="outcome"/> (e.g. <see cref="ReleaseLookup.Unauthorized"/>) and returns this instance for chaining.</summary>
    /// <param name="outcome">The lookup outcome to force regardless of <see cref="Releases"/>.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithReleaseOutcome(ReleaseLookup outcome)
    {
        ReleaseOutcome = outcome;
        return this;
    }

    /// <summary>Sets the canned <see cref="GetLatestWorkflowRunAsync"/> outcome and returns this instance for chaining.</summary>
    /// <param name="result">The workflow-run outcome to return for every call.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithWorkflowRun(BuildRunCheck result)
    {
        WorkflowRun = result;
        return this;
    }

    /// <summary>Sets the branch head to <paramref name="commitSha"/> and returns this instance for chaining.</summary>
    /// <param name="commitSha">The commit every <see cref="GetBranchHeadAsync"/> call reports as found.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithHead(string commitSha)
    {
        Head = new BranchHead { Outcome = RepositoryLookup.Found, CommitSha = commitSha };
        return this;
    }

    /// <summary>Forces every <see cref="GetBranchHeadAsync"/> call to report <paramref name="outcome"/> without a commit, and returns this instance for chaining.</summary>
    /// <param name="outcome">The lookup outcome to force (e.g. <see cref="RepositoryLookup.Unauthorized"/>).</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithHeadOutcome(RepositoryLookup outcome)
    {
        Head = BranchHead.Missing(outcome);
        return this;
    }

    /// <summary>Forces every <see cref="GetTreeAsync"/> call to report <paramref name="outcome"/> and returns this instance for chaining.</summary>
    /// <param name="outcome">The lookup outcome to force; <see cref="RepositoryLookup.Found"/> lists <see cref="RepositoryFiles"/>.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithTreeOutcome(RepositoryLookup outcome)
    {
        TreeOutcome = outcome;
        return this;
    }

    /// <summary>Adds or replaces the file at <paramref name="path"/> and returns this instance for chaining.</summary>
    /// <param name="path">The repo-relative file path.</param>
    /// <param name="content">The file's raw bytes.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithRepositoryFile(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        RepositoryFiles[path] = content;
        return this;
    }

    /// <summary>Adds or replaces the UTF-8 text file at <paramref name="path"/> and returns this instance for chaining.</summary>
    /// <param name="path">The repo-relative file path.</param>
    /// <param name="text">The file's text, stored as UTF-8.</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithRepositoryFile(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return WithRepositoryFile(path, System.Text.Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Forces every <see cref="ReadFileAsync"/> call to report <paramref name="outcome"/> and returns this instance for chaining.</summary>
    /// <param name="outcome">The read outcome to force (e.g. <see cref="FileRead.Unauthorized"/>).</param>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithFileReadOutcome(FileRead outcome)
    {
        FileReadOutcome = outcome;
        return this;
    }

    /// <inheritdoc />
    public Task<RepoCheck> RepoExistsAsync(string repo, CancellationToken ct) => Task.FromResult(Repo);

    /// <inheritdoc />
    public Task<FileCheck> FileExistsAsync(string repo, string path, CancellationToken ct) => Task.FromResult(File);

    /// <inheritdoc />
    public Task<ReleaseList> GetLatestReleaseAsync(string repo, CancellationToken ct)
    {
        if (ReleaseOutcome is not ReleaseLookup.Found)
            return Task.FromResult(ReleaseList.Empty(ReleaseOutcome));
        if (Releases.Count == 0)
            return Task.FromResult(ReleaseList.Empty(ReleaseLookup.None));

        return Task.FromResult(new ReleaseList { Outcome = ReleaseLookup.Found, Releases = [Releases[0]] });
    }

    /// <inheritdoc />
    public Task<ReleaseList> GetReleasesAsync(string repo, int limit, CancellationToken ct)
    {
        if (ReleaseOutcome is not ReleaseLookup.Found)
            return Task.FromResult(ReleaseList.Empty(ReleaseOutcome));
        if (Releases.Count == 0)
            return Task.FromResult(ReleaseList.Empty(ReleaseLookup.None));

        var page = Releases.Take(limit).ToList();
        return Task.FromResult(new ReleaseList { Outcome = ReleaseLookup.Found, Releases = page });
    }

    /// <inheritdoc />
    public Task<BuildRunCheck> GetLatestWorkflowRunAsync(string repo, string workflowFile, CancellationToken ct) => Task.FromResult(WorkflowRun);

    /// <inheritdoc />
    public Task<BranchHead> GetBranchHeadAsync(string repo, string branch, CancellationToken ct) => Task.FromResult(Head);

    /// <inheritdoc />
    public Task<RepositoryTree> GetTreeAsync(string repo, string commitSha, CancellationToken ct)
    {
        if (TreeOutcome is not RepositoryLookup.Found)
            return Task.FromResult(RepositoryTree.Empty(TreeOutcome));

        var entries = RepositoryFiles
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new RepositoryTreeEntry { Path = file.Key, Type = RepositoryTreeEntryType.Blob, Size = file.Value.Length })
            .ToList();
        return Task.FromResult(new RepositoryTree { Outcome = RepositoryLookup.Found, Entries = entries });
    }

    /// <inheritdoc />
    public Task<RepositoryFile> ReadFileAsync(string repo, string commitSha, string path, int maxBytes, CancellationToken ct)
    {
        if (FileReadOutcome is { } forced and not FileRead.Found)
            return Task.FromResult(RepositoryFile.Missing(forced));
        if (!RepositoryFiles.TryGetValue(path, out var content))
            return Task.FromResult(RepositoryFile.Missing(FileRead.NotFound));
        if (content.Length > maxBytes)
            return Task.FromResult(RepositoryFile.Missing(FileRead.TooLarge));

        return Task.FromResult(new RepositoryFile { Outcome = FileRead.Found, Content = content });
    }
}
