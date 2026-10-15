namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>The outcome of a recursive tree listing at one commit — the categorized result plus every entry GitHub returned.</summary>
public sealed record RepositoryTree
{
    /// <summary>How the lookup resolved.</summary>
    public required RepositoryLookup Outcome { get; init; }

    /// <summary>Every blob, tree and submodule entry, in GitHub's order; empty unless <see cref="Outcome"/> is <see cref="RepositoryLookup.Found"/>.</summary>
    public required IReadOnlyList<RepositoryTreeEntry> Entries { get; init; }

    /// <summary>Whether GitHub cut the listing short at its size limit, so <see cref="Entries"/> is incomplete.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>An empty tree result for a non-<see cref="RepositoryLookup.Found"/> outcome.</summary>
    /// <param name="outcome">The categorized lookup outcome.</param>
    /// <returns>A <see cref="RepositoryTree"/> carrying no entries.</returns>
    public static RepositoryTree Empty(RepositoryLookup outcome)
    {
        return new RepositoryTree { Outcome = outcome, Entries = [] };
    }
}
