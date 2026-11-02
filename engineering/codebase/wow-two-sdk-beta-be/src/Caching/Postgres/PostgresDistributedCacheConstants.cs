namespace WoW.Two.Sdk.Beta.Be.Caching.Postgres;

/// <summary>Holds the fixed names of the PostgreSQL distributed cache: its migration set, defaults, sweep job and limits.</summary>
public static class PostgresDistributedCacheConstants
{
    /// <summary>The migration set that creates the cache table; it also keys the cache's data source and connection factory.</summary>
    public const string MigrationSetName = "distributed_cache";

    /// <summary>The owner kind the set claims its schema as.</summary>
    public const string SchemaOwner = "module";

    /// <summary>The schema used when none is configured.</summary>
    public const string DefaultSchema = "cache";

    /// <summary>The cache table used when none is configured; the embedded migration names it literally.</summary>
    public const string DefaultTableName = "distributed_cache";

    /// <summary>The periodic job that deletes expired rows.</summary>
    public const string SweepJobName = "distributed_cache_sweep";

    /// <summary>The resource prefix of the set's embedded <c>NNN-name/{Apply,Rollback}.sql</c> folders.</summary>
    public const string MigrationResourcePrefix = "Caching/Postgres/Migrations/";

    /// <summary>The largest key, in UTF-8 bytes; a B-tree index entry must fit a third of a page.</summary>
    public const int MaxKeyBytes = 2048;

    /// <summary>The most expired rows one sweep statement deletes; a sweep repeats it until fewer remain.</summary>
    public const int SweepBatchSize = 1000;
}
