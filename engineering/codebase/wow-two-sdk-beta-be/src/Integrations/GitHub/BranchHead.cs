namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>The outcome of reading a branch's newest commit — the categorized result plus the commit SHA when found.</summary>
public sealed record BranchHead
{
    /// <summary>How the lookup resolved.</summary>
    public required RepositoryLookup Outcome { get; init; }

    /// <summary>The full lowercase commit SHA; <see langword="null"/> unless <see cref="Outcome"/> is <see cref="RepositoryLookup.Found"/>.</summary>
    public string? CommitSha { get; init; }

    /// <summary>A head result carrying no commit, for a non-<see cref="RepositoryLookup.Found"/> outcome.</summary>
    /// <param name="outcome">The categorized lookup outcome.</param>
    /// <returns>A <see cref="BranchHead"/> without a commit SHA.</returns>
    public static BranchHead Missing(RepositoryLookup outcome)
    {
        return new BranchHead { Outcome = outcome };
    }
}
