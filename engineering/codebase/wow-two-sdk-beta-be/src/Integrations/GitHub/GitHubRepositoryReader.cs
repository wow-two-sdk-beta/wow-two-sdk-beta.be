using WoW.Two.Sdk.Beta.Be.Caching.Core;

namespace WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

/// <summary>Default <see cref="IGitHubRepositoryReader"/>: the GitHub client's reads, kept per commit in the SDK cache.</summary>
/// <remarks>
///   - register with <c>AddGitHubRepositoryReader</c>; it needs <see cref="IGitHubClient"/> (<c>AddGitHubIntegration</c>)
///     and <see cref="ICacheRepository"/> (<c>AddHybridCaching</c>)
///   - a head is kept under <see cref="HeadKey"/>, so a caller can evict it; tree and file entries are keyed by commit
/// </remarks>
/// <param name="github">The client every read goes through.</param>
/// <param name="cache">The cache heads, trees and files are kept in.</param>
/// <param name="options">How long heads and commits are kept; <see langword="null"/> keeps the defaults.</param>
public sealed class GitHubRepositoryReader(
    IGitHubClient github, ICacheRepository cache, GitHubRepositoryReaderOptions? options = null) : IGitHubRepositoryReader
{
    private readonly CacheEntryOptions? _headEntry = Entry((options ?? new GitHubRepositoryReaderOptions()).HeadFor);

    private readonly CacheEntryOptions _commitEntry =
        Entry((options ?? new GitHubRepositoryReaderOptions()).CommitFor) ?? throw new ArgumentException(
            "GitHubRepositoryReaderOptions.CommitFor must be positive.", nameof(options));

    /// <summary>Names the cache entry that keeps a branch's head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch.</param>
    /// <returns>The cache key.</returns>
    public static string HeadKey(string repository, string branch) => $"github-head:{repository}@{branch}";

    /// <inheritdoc />
    public async Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        if (_headEntry is null)
            return (await github.GetBranchHeadAsync(repository, branch, ct).ConfigureAwait(false)).CommitSha;

        var key = HeadKey(repository, branch);
        if (!fresh)
        {
            var kept = await cache.GetOrCreateAsync(
                key,
                async token => await github.GetBranchHeadAsync(repository, branch, token).ConfigureAwait(false),
                _headEntry,
                ct).ConfigureAwait(false);
            return kept.CommitSha;
        }

        var head = await github.GetBranchHeadAsync(repository, branch, ct).ConfigureAwait(false);
        if (head.CommitSha is not null)
            await cache.SetAsync(key, head, _headEntry, ct).ConfigureAwait(false);
        return head.CommitSha;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> ListAsync(
        string repository, string commit, string purpose, Func<string, long?, bool> keep, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(keep);

        return await cache.GetOrCreateUnlessTransientAsync<IReadOnlyList<string>?>(
            $"github-tree:{purpose}:{repository}@{commit}",
            async token =>
            {
                var tree = await github.GetTreeAsync(repository, commit, token).ConfigureAwait(false);
                if (tree.Outcome != RepositoryLookup.Found)
                    return CacheResult.Transient<IReadOnlyList<string>?>(null);

                return CacheResult.Keep<IReadOnlyList<string>?>(
                [
                    .. tree.Entries
                        .Where(entry => entry.Type == RepositoryTreeEntryType.Blob && keep(entry.Path, entry.Size))
                        .Select(entry => entry.Path),
                ]);
            },
            _commitEntry,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RepositoryFile> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return await cache.GetOrCreateUnlessTransientAsync(
            $"github-file:{repository}@{commit}:{path}:{maxBytes}",
            async token =>
            {
                var file = await github.ReadFileAsync(repository, commit, path, maxBytes, token).ConfigureAwait(false);
                // A verdict about a commit is final; no verdict is read again next time.
                return file.Outcome is FileRead.Found or FileRead.NotFound or FileRead.TooLarge
                    ? CacheResult.Keep(file)
                    : CacheResult.Transient(file);
            },
            _commitEntry,
            ct).ConfigureAwait(false);
    }

    /// <summary>The entry options that keep a value for <paramref name="lifetime"/>; none when it is not positive.</summary>
    private static CacheEntryOptions? Entry(TimeSpan lifetime) =>
        lifetime > TimeSpan.Zero ? new CacheEntryOptions { Expiration = lifetime, LocalCacheExpiration = lifetime } : null;
}
