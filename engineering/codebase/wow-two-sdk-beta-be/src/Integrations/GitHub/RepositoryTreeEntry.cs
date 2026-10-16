namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>One entry of a recursive repository tree listing.</summary>
public sealed record RepositoryTreeEntry
{
    /// <summary>The repository-relative path, <c>/</c>-separated (e.g. <c>src/app/main.cs</c>).</summary>
    public required string Path { get; init; }

    /// <summary>What the entry points at.</summary>
    public required RepositoryTreeEntryType Type { get; init; }

    /// <summary>The size in bytes; reported for blobs only.</summary>
    public long? Size { get; init; }
}
