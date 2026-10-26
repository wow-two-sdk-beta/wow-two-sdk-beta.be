namespace WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness;

/// <summary>Specifies what an upsert did to the row its key names.</summary>
public enum UpsertAction
{
    /// <summary>No row had the key; the candidate was inserted.</summary>
    Inserted,

    /// <summary>A row had the key, or another writer inserted it first; the row was updated.</summary>
    Updated,

    /// <summary>A row had the key, or another writer inserted it first; the row was left as it was.</summary>
    Unchanged,
}
