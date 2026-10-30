namespace WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

/// <summary>Holds how long the <see cref="IGitHubRepositoryReader"/> keeps a branch head and what it read at a commit.</summary>
public sealed record GitHubRepositoryReaderOptions
{
    /// <summary>The host configuration section applied after code configuration.</summary>
    public const string SectionName = "Integrations:GitHub:Reader";

    /// <summary>How long a branch head is trusted before it is read again; a pushed change shows within this time.
    /// Default five minutes; <see cref="TimeSpan.Zero"/> reads the head on every call, as a test host that pushes
    /// commits wants.</summary>
    public TimeSpan HeadFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a commit's tree and files stay cached. Default one day; a commit never changes, so this only
    /// bounds the memory a branch that moved on still holds.</summary>
    public TimeSpan CommitFor { get; set; } = TimeSpan.FromDays(1);
}
