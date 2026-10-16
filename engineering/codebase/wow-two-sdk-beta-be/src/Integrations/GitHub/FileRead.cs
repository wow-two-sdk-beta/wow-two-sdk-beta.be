namespace WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

/// <summary>Refers to outcome of reading one file at one commit — separates a missing file from an oversized one, one the token can't see, or a transport failure.</summary>
public enum FileRead
{
    /// <summary>The file was read within the byte limit → <see cref="RepositoryFile.Content"/> is populated.</summary>
    Found,

    /// <summary>No such file, commit or repo → 404.</summary>
    NotFound,

    /// <summary>The file is larger than the caller's byte limit; its content was not kept.</summary>
    TooLarge,

    /// <summary>The token is missing/expired or can't see the repo → 401/403.</summary>
    Unauthorized,

    /// <summary>The read could not be completed (transport error or an unexpected status).</summary>
    Failed
}
