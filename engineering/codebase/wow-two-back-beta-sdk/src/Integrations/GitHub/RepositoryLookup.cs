namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>Refers to outcome of reading a branch head or a commit tree — separates a missing ref from one the token can't see or a transport failure.</summary>
public enum RepositoryLookup
{
    /// <summary>The ref resolved and its content was read → 200.</summary>
    Found,

    /// <summary>No such repo, branch or commit, or the repository is empty → 404 / 409 / 422.</summary>
    NotFound,

    /// <summary>The token is missing/expired or can't see the repo → 401/403.</summary>
    Unauthorized,

    /// <summary>The lookup could not be completed (transport error, an unexpected status or an unreadable body).</summary>
    Failed
}
