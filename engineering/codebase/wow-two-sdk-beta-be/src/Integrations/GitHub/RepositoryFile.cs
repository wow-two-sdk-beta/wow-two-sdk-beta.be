namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>The outcome of reading one repository file at one commit — the categorized result plus the raw bytes when found.</summary>
public sealed record RepositoryFile
{
    /// <summary>How the read resolved.</summary>
    public required FileRead Outcome { get; init; }

    /// <summary>The file's raw bytes; <see langword="null"/> unless <see cref="Outcome"/> is <see cref="FileRead.Found"/>.</summary>
    public byte[]? Content { get; init; }

    /// <summary>A file result carrying no content, for a non-<see cref="FileRead.Found"/> outcome.</summary>
    /// <param name="outcome">The categorized read outcome.</param>
    /// <returns>A <see cref="RepositoryFile"/> without content.</returns>
    public static RepositoryFile Missing(FileRead outcome)
    {
        return new RepositoryFile { Outcome = outcome };
    }
}
