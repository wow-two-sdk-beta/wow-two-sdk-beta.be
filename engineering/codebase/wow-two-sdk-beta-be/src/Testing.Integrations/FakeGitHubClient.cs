using System.Globalization;
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
///   - each canned setting is one outcome for every call, never varying per repo; only
///     <see cref="RepositoryFiles"/> varies per path, and it drives both the tree listing and the file reads
///   - several repositories and heads: <see cref="Push(string, string, string, string, string)"/> moves a branch to a
///     new commit that keeps the branch's other files, as a developer's push does; a pushed repository answers from its
///     commits only, every other repository from the canned settings (or as missing, with <see cref="PushedOnly"/>)
///   - every head, tree and file read is recorded (<see cref="HeadReads"/>, <see cref="TreeReads"/>, <see cref="FileReads"/>)
///   - the pushed state and the records are thread-safe, for a test host serving requests at once
/// </remarks>
public sealed class FakeGitHubClient : IGitHubClient
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BranchHead> _branches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, PushedFile>> _commits = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pushed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _headReads = [];
    private readonly List<string> _treeReads = [];
    private readonly List<string> _fileReads = [];
    private int _commitCount;

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

    /// <summary>Whether a repository nobody pushed reads as not found, instead of answering from the canned
    /// <see cref="Head"/> and <see cref="RepositoryFiles"/>. Defaults to <see langword="false"/>.</summary>
    public bool PushedOnly { get; set; }

    /// <summary>Gets or sets an exception every head, tree and file read throws, for a client that fails unexpectedly; the
    /// read is still recorded. Defaults to <see langword="null"/>.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets every head read, as <c>{repository}@{branch}</c>, in the order asked.</summary>
    public IReadOnlyList<string> HeadReads
    {
        get
        {
            lock (_lock)
                return [.. _headReads];
        }
    }

    /// <summary>Gets every tree read, as <c>{repository}@{commit}</c>, in the order asked.</summary>
    public IReadOnlyList<string> TreeReads
    {
        get
        {
            lock (_lock)
                return [.. _treeReads];
        }
    }

    /// <summary>Gets every file read, as <c>{repository}@{commit}:{path}</c>, in the order asked.</summary>
    public IReadOnlyList<string> FileReads
    {
        get
        {
            lock (_lock)
                return [.. _fileReads];
        }
    }

    /// <summary>Makes a repository nobody pushed read as not found, and returns this instance for chaining.</summary>
    /// <returns>This <see cref="FakeGitHubClient"/>.</returns>
    public FakeGitHubClient WithPushedOnly()
    {
        PushedOnly = true;
        return this;
    }

    /// <summary>Pushes a commit that adds or replaces one UTF-8 text file, keeping the branch's other files, and makes it the branch's head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="path">The repo-relative file path.</param>
    /// <param name="text">The file's text, stored as UTF-8.</param>
    /// <param name="branch">The branch to move.</param>
    /// <param name="commit">The commit SHA to push as; <see langword="null"/> makes a new 40-digit one. Naming a commit the
    /// repository already holds adds the file to it.</param>
    /// <returns>The commit SHA the branch now points at.</returns>
    public string Push(string repository, string path, string text, string branch = "main", string? commit = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Push(repository, path, System.Text.Encoding.UTF8.GetBytes(text), branch, commit);
    }

    /// <summary>Pushes a commit that adds or replaces one file, keeping the branch's other files, and makes it the branch's head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="path">The repo-relative file path.</param>
    /// <param name="content">The file's raw bytes.</param>
    /// <param name="branch">The branch to move.</param>
    /// <param name="commit">The commit SHA to push as; <see langword="null"/> makes a new 40-digit one.</param>
    /// <returns>The commit SHA the branch now points at.</returns>
    public string Push(string repository, string path, byte[] content, string branch = "main", string? commit = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Commit(repository, branch, commit, path, new PushedFile(FileRead.Found, content));
    }

    /// <summary>Pushes a commit whose file reads as <paramref name="outcome"/> (too large, unauthorized, failed) and makes it the branch's head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="path">The repo-relative file path.</param>
    /// <param name="outcome">What reading the file gives at that commit; <see cref="FileRead.NotFound"/> removes the file.</param>
    /// <param name="branch">The branch to move.</param>
    /// <param name="commit">The commit SHA to push as; <see langword="null"/> makes a new 40-digit one.</param>
    /// <returns>The commit SHA the branch now points at.</returns>
    /// <exception cref="ArgumentException"><paramref name="outcome"/> is <see cref="FileRead.Found"/>, which needs content.</exception>
    public string Push(string repository, string path, FileRead outcome, string branch = "main", string? commit = null)
    {
        if (outcome is FileRead.Found)
            throw new ArgumentException("A found file needs its content; push the bytes or the text instead.", nameof(outcome));
        return Commit(repository, branch, commit, path, new PushedFile(outcome, null));
    }

    /// <summary>Pushes a commit with no files, so the branch reads but carries nothing.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to move.</param>
    /// <returns>The new commit's SHA.</returns>
    public string PushEmpty(string repository, string branch = "main")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        lock (_lock)
        {
            var commit = NewCommit();
            _commits[CommitKey(repository, commit)] = new Dictionary<string, PushedFile>(StringComparer.Ordinal);
            _branches[BranchKey(repository, branch)] = Found(commit);
            _pushed.Add(repository);
            return commit;
        }
    }

    /// <summary>Makes a pushed repository's branch read as <paramref name="outcome"/>, as when GitHub is down or refuses the
    /// token; its commits stay readable. The next push to the branch makes it readable again, carrying only that push's file.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="outcome">What the head read reports.</param>
    public void Unreachable(string repository, string branch = "main", RepositoryLookup outcome = RepositoryLookup.Failed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        if (outcome is RepositoryLookup.Found)
            throw new ArgumentException("An unreachable branch reads as anything but found.", nameof(outcome));

        lock (_lock)
        {
            _branches[BranchKey(repository, branch)] = BranchHead.Missing(outcome);
            _pushed.Add(repository);
        }
    }

    /// <summary>Forgets every pushed repository and every recorded read; the canned settings stay as they are.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _branches.Clear();
            _commits.Clear();
            _pushed.Clear();
            _headReads.Clear();
            _treeReads.Clear();
            _fileReads.Clear();
        }
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
    public Task<BranchHead> GetBranchHeadAsync(string repo, string branch, CancellationToken ct)
    {
        lock (_lock)
        {
            _headReads.Add(BranchKey(repo, branch));
            if (Failure is { } failure)
                throw failure;
            if (_branches.TryGetValue(BranchKey(repo, branch), out var pushed))
                return Task.FromResult(pushed);
            if (_pushed.Contains(repo) || PushedOnly)
                return Task.FromResult(BranchHead.Missing(RepositoryLookup.NotFound));
        }

        return Task.FromResult(Head);
    }

    /// <inheritdoc />
    public Task<RepositoryTree> GetTreeAsync(string repo, string commitSha, CancellationToken ct)
    {
        lock (_lock)
        {
            _treeReads.Add(CommitKey(repo, commitSha));
            if (Failure is { } failure)
                throw failure;
            if (TreeOutcome is not RepositoryLookup.Found)
                return Task.FromResult(RepositoryTree.Empty(TreeOutcome));
            if (_commits.TryGetValue(CommitKey(repo, commitSha), out var files))
            {
                var listed = files
                    .Where(file => file.Value.Outcome is not FileRead.NotFound)
                    .OrderBy(file => file.Key, StringComparer.Ordinal)
                    .Select(file => new RepositoryTreeEntry
                    {
                        Path = file.Key, Type = RepositoryTreeEntryType.Blob, Size = file.Value.Content?.Length,
                    })
                    .ToList();
                return Task.FromResult(new RepositoryTree { Outcome = RepositoryLookup.Found, Entries = listed });
            }

            if (_pushed.Contains(repo) || PushedOnly)
                return Task.FromResult(RepositoryTree.Empty(RepositoryLookup.NotFound));
        }

        var entries = RepositoryFiles
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new RepositoryTreeEntry { Path = file.Key, Type = RepositoryTreeEntryType.Blob, Size = file.Value.Length })
            .ToList();
        return Task.FromResult(new RepositoryTree { Outcome = RepositoryLookup.Found, Entries = entries });
    }

    /// <inheritdoc />
    public Task<RepositoryFile> ReadFileAsync(string repo, string commitSha, string path, int maxBytes, CancellationToken ct)
    {
        lock (_lock)
        {
            _fileReads.Add($"{CommitKey(repo, commitSha)}:{path}");
            if (Failure is { } failure)
                throw failure;
            if (FileReadOutcome is { } forced and not FileRead.Found)
                return Task.FromResult(RepositoryFile.Missing(forced));
            if (_commits.TryGetValue(CommitKey(repo, commitSha), out var files))
            {
                if (!files.TryGetValue(path, out var file))
                    return Task.FromResult(RepositoryFile.Missing(FileRead.NotFound));
                if (file.Content is not { } bytes)
                    return Task.FromResult(RepositoryFile.Missing(file.Outcome));
                return Task.FromResult(bytes.Length > maxBytes
                    ? RepositoryFile.Missing(FileRead.TooLarge)
                    : new RepositoryFile { Outcome = FileRead.Found, Content = bytes });
            }

            if (_pushed.Contains(repo) || PushedOnly)
                return Task.FromResult(RepositoryFile.Missing(FileRead.NotFound));
        }

        if (!RepositoryFiles.TryGetValue(path, out var content))
            return Task.FromResult(RepositoryFile.Missing(FileRead.NotFound));
        if (content.Length > maxBytes)
            return Task.FromResult(RepositoryFile.Missing(FileRead.TooLarge));

        return Task.FromResult(new RepositoryFile { Outcome = FileRead.Found, Content = content });
    }

    private static string BranchKey(string repository, string branch) => $"{repository}@{branch}";

    private static string CommitKey(string repository, string commit) => $"{repository}@{commit}";

    private static BranchHead Found(string commit) => new() { Outcome = RepositoryLookup.Found, CommitSha = commit };

    /// <summary>Makes the next 40-digit commit SHA; the caller holds the lock.</summary>
    private string NewCommit() => (++_commitCount).ToString("x40", CultureInfo.InvariantCulture);

    /// <summary>Puts one file on a commit — the named one, else a new one carrying the branch head's files — and moves the branch there.</summary>
    private string Commit(string repository, string branch, string? commit, string path, PushedFile file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        lock (_lock)
        {
            commit ??= NewCommit();
            if (!_commits.TryGetValue(CommitKey(repository, commit), out var files))
            {
                files = _branches.TryGetValue(BranchKey(repository, branch), out var head)
                    && head.CommitSha is { } current
                    && _commits.TryGetValue(CommitKey(repository, current), out var carried)
                        ? new Dictionary<string, PushedFile>(carried, StringComparer.Ordinal)
                        : new Dictionary<string, PushedFile>(StringComparer.Ordinal);
                _commits[CommitKey(repository, commit)] = files;
            }

            if (file.Outcome is FileRead.NotFound)
                files.Remove(path);
            else
                files[path] = file;
            _branches[BranchKey(repository, branch)] = Found(commit);
            _pushed.Add(repository);
            return commit;
        }
    }

    /// <summary>One pushed file: its content when it reads, else the outcome its read reports.</summary>
    private sealed record PushedFile(FileRead Outcome, byte[]? Content);
}
