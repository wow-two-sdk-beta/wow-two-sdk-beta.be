namespace WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

/// <summary>Refers to kind of object a tree entry points at, as the Git trees API reports it.</summary>
public enum RepositoryTreeEntryType
{
    /// <summary>A file → <c>blob</c>.</summary>
    Blob,

    /// <summary>A directory → <c>tree</c>.</summary>
    Tree,

    /// <summary>A submodule pinned at a commit → <c>commit</c>.</summary>
    Commit
}
