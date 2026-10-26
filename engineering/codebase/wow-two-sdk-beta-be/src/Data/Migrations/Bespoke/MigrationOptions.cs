namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds configuration for one migration set, shared by every host that runs it.</summary>
public sealed record MigrationOptions
{
    /// <summary>Gets the set these options configure: <see cref="MigrationConstants.DefaultSetName"/> or the name passed to <c>AddDatabaseBespokeMigrationSet</c>.</summary>
    public string SetName { get; internal set; } = MigrationConstants.DefaultSetName;

    /// <summary>Gets or sets the version label stamped onto applied rows.</summary>
    public string Version { get; set; } = "v1.0";

    /// <summary>Gets or sets whether rollback and repair are permitted.</summary>
    public bool AllowRollback { get; set; }

    /// <summary>Gets or sets whether apply tolerates orphaned history (applied rows with no source migration).</summary>
    public bool AllowOrphanedHistory { get; set; }

    /// <summary>Gets or sets how pending transactional migrations share transactions during apply.</summary>
    /// <remarks>The default <see cref="MigrationTransactionScope.Batch"/> keeps a failed run from leaving part of a batch applied.</remarks>
    public MigrationTransactionScope TransactionScope { get; set; } = MigrationTransactionScope.Batch;

    /// <summary>Gets or sets the database provider, which selects the SQL dialect.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    /// <summary>Gets or sets the set's schema: it holds the history table and is the value of <see cref="MigrationConstants.SchemaPlaceholder"/>.</summary>
    /// <remarks>SQLite has no schemas: the history table ignores it, and the placeholder drops it (keep set tables apart with a prefix).</remarks>
    public string SchemaName { get; set; } = "public";

    /// <summary>Gets or sets the migration-history table name.</summary>
    /// <remarks>A named set defaults to <c>{SetName}_migration_history</c>, so its history never mixes with another set's.</remarks>
    public string TableName { get; set; } = "migration_history";

    /// <summary>Gets or sets the advisory-lock id shared by every host of this set, so only one apply loop runs at a time.</summary>
    /// <remarks>A named set defaults to an id derived from its name, so sets never wait on each other.</remarks>
    public long AdvisoryLockId { get; set; } = 4_855_178_001L;

    /// <summary>Gets or sets the owner this host claims <see cref="SchemaName"/> under, such as <c>module</c> or <c>service</c>; null claims nothing.</summary>
    /// <remarks>
    ///   - when set, the set creates its schema on PostgreSQL if missing, and its first apply records a marker row naming the set and owner
    ///   - every run then refuses, before any script, a schema whose marker names another set or owner
    ///   - a set without an owner still refuses a schema another set has claimed
    /// </remarks>
    public string? SchemaOwner { get; set; }

    /// <summary>Gets or sets how the startup level check treats this set when the database is behind, ahead of or drifted from the code.</summary>
    /// <remarks>An owner conflict always fails, whatever the policy.</remarks>
    public MigrationLevelPolicy LevelMismatch { get; set; } = MigrationLevelPolicy.Fail;
}
