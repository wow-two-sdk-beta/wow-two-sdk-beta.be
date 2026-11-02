namespace WoW.Two.Sdk.Beta.Be.Caching.Postgres;

/// <summary>Holds options for the PostgreSQL-backed <c>IDistributedCache</c> registered by <see cref="PostgresDistributedCacheServiceCollectionExtensions.AddPostgresDistributedCache"/>.</summary>
/// <remarks>Set in code with <c>AddPostgresDistributedCache(o => …)</c> or in the host section <c>Caching:Postgres</c>, which is applied last.</remarks>
public sealed record PostgresDistributedCacheOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Caching:Postgres";

    /// <summary>Gets or sets the connection string of the database that holds the cache table. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the schema the cache's migration set owns; a lowercase identifier. Defaults to <c>cache</c>.</summary>
    /// <remarks>Two applications sharing one database each set their own schema: the set claims its schema for one owner instance.</remarks>
    public string Schema { get; set; } = PostgresDistributedCacheConstants.DefaultSchema;

    /// <summary>Gets or sets the cache table; a lowercase identifier. Defaults to <c>distributed_cache</c>.</summary>
    /// <remarks>
    ///   - fixed once the set is applied: a new name changes the migration's checksum, so it needs a new schema
    ///   - only the default name applies through <c>wow-migrate</c> reading the SDK assembly; any name applies through the image's own <c>app migrate</c>
    /// </remarks>
    public string TableName { get; set; } = PostgresDistributedCacheConstants.DefaultTableName;

    /// <summary>Gets or sets the sliding expiration of an entry set without any expiration. Defaults to 20 minutes.</summary>
    public TimeSpan DefaultSlidingExpiration { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Gets or sets the time between sweeps that delete expired rows. Defaults to 30 minutes.</summary>
    public TimeSpan ExpiredItemsDeletionInterval { get; set; } = TimeSpan.FromMinutes(30);
}
