namespace WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

/// <summary>
/// Defines behavior that reads a repository through <see cref="IGitHubClient"/> one commit at a time, keeping what it
/// read per commit: a commit's tree and files never change, so only a branch head is read again.
/// </summary>
/// <remarks>
///   - every consumer of a repository shares one cache and one rule; what a file means, and which files matter, stays
///     with the caller
///   - a head is kept for <see cref="GitHubRepositoryReaderOptions.HeadFor"/>, so a pushed change shows within that time
///   - a verdict about a commit — found, missing, too large — is kept; no verdict (unauthorized, failed) is read again
/// </remarks>
public interface IGitHubRepositoryReader
{
    /// <summary>Reads the branch's newest commit; a head that could not be read is kept as long as one that could, so a
    /// repository without access costs no GitHub call per page.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch, or any ref GitHub resolves to a commit.</param>
    /// <param name="fresh">Whether to skip a kept head and read it now; a fresh read that fails leaves the kept head as it was.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The full commit SHA, or <see langword="null"/> when the branch cannot be read.</returns>
    Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct);

    /// <summary>Lists the blobs of a commit the caller keeps, kept per commit under the caller's own purpose name.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="purpose">Names what the list is for; it keys the cache, so two callers keep their own lists.</param>
    /// <param name="keep">Decides from a blob's path and size, when GitHub gave one, whether to keep it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The kept paths, or <see langword="null"/> when GitHub gave no tree; no tree is read again next time.</returns>
    Task<IReadOnlyList<string>?> ListAsync(
        string repository, string commit, string purpose, Func<string, long?, bool> keep, CancellationToken ct);

    /// <summary>Reads one file as it was at a commit, at most <paramref name="maxBytes"/> of it.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="maxBytes">The largest file to read; a larger one is <see cref="FileRead.TooLarge"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The read; <see cref="FileRead.Found"/>, <see cref="FileRead.NotFound"/> and <see cref="FileRead.TooLarge"/> are
    /// kept for the commit, any other outcome is read again next time.</returns>
    Task<RepositoryFile> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct);
}
