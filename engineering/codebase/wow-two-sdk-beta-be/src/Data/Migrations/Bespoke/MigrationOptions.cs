namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds configuration for one migration set, shared by every host that runs it.</summary>
public sealed record MigrationOptions
{
    /// <summary>Holds the history-table suffix the seed journal's name replaces.</summary>
    private const string HistorySuffix = "migration_history";

    /// <summary>Gets the set these options configure: <see cref="MigrationConstants.DefaultSetName"/> or the name passed to <c>AddDatabaseBespokeMigrationSet</c>.</summary>
    public string SetName { get; internal set; } = MigrationConstants.DefaultSetName;

    /// <summary>Gets or sets the version label stamped onto applied rows.</summary>
    public string Version { get; set; } = "v1.0";

    /// <summary>Gets or sets the owner release this image carries, such as <c>0.5.13</c>, which a contract's <c>since</c> compares with.</summary>
    /// <remarks>
    ///   - null at registration derives it from the assembly that embeds the set: its informational version, build metadata stripped
    ///   - a set read from the filesystem or a custom source has none unless set here; it then counts every contract above its level as incompatible
    /// </remarks>
    public string? Release { get; set; }

    /// <summary>Gets or sets whether rollback and repair are permitted.</summary>
    /// <remarks>Gates <see cref="IMigrationRunnerService.RollbackAsync"/> and <see cref="IMigrationRunnerService.RepairAsync"/> only; the confirmed verbs the command tree runs carry their own guard.</remarks>
    public bool AllowRollback { get; set; }

    /// <summary>Gets or sets whether apply tolerates every orphaned history row (applied rows with no source migration), compatible or not.</summary>
    /// <remarks>A compatible <see cref="MigrationLevelState.AheadCompatible"/> needs no opt-in; this also tolerates orphans below the level and an incompatible ahead.</remarks>
    public bool AllowOrphanedHistory { get; set; }

    /// <summary>Gets or sets whether this is a local run, which may apply a pending ordinal below the applied level.</summary>
    /// <remarks>Null derives it: <c>Development</c> (configuration <c>environment</c>, else <c>DOTNET_ENVIRONMENT</c> or <c>ASPNETCORE_ENVIRONMENT</c>) without <c>DEPLOY_ENVIRONMENT</c>.</remarks>
    public bool? LocalRun { get; set; }

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
    public string TableName { get; set; } = HistorySuffix;

    /// <summary>Gets the set's append-only event table: <c>{TableName}_events</c>.</summary>
    public string EventsTableName => $"{TableName}_events";

    /// <summary>Gets the set's seed journal: <c>seed_history</c> for the default set, <c>{SetName}_seed_history</c> for a named set.</summary>
    public string SeedHistoryTableName => TableName.EndsWith(HistorySuffix, StringComparison.Ordinal)
        ? TableName[..^HistorySuffix.Length] + "seed_history"
        : TableName + "_seeds";

    /// <summary>Gets or sets the id every host of this set derives its advisory locks from, so only one migrator runs at a time.</summary>
    /// <remarks>
    ///   - a named set defaults to an id derived from its name, so sets never wait on each other
    ///   - the migrator takes <see cref="MigrationLockKey.Reserved"/> in the reserved class, and the id itself while <see cref="TakeLegacyAdvisoryLock"/> holds
    /// </remarks>
    public long AdvisoryLockId { get; set; } = 4_855_178_001L;

    /// <summary>Gets or sets whether the migrator also takes the legacy single bigint key, so an applicant built before the reserved class still waits for it.</summary>
    /// <remarks>On for the transition: an app lock on the same bigint then delays the migrator; turn it off once no applicant below the reserved class remains.</remarks>
    public bool TakeLegacyAdvisoryLock { get; set; } = true;

    /// <summary>Gets or sets how long the migrator waits for its advisory locks before it reports busy.</summary>
    public TimeSpan AdvisoryLockWait { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets how long each script waits for a table lock (<c>lock_timeout</c>); a file's <c>-- @lock-timeout</c> overrides it.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how many times a batch runs when a table lock times out before the migrator reports busy.</summary>
    public int LockAttempts { get; set; } = 3;

    /// <summary>Gets or sets the pause before a retried batch, multiplied by the attempt number.</summary>
    public TimeSpan LockRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the kind of owner this host claims <see cref="SchemaName"/> as, such as <c>module</c> or <c>service</c>; null claims nothing.</summary>
    /// <remarks>
    ///   - when set, the set creates its schema on PostgreSQL if missing, and its first apply records a marker row naming the set, owner and <see cref="OwnerInstance"/>
    ///   - every run then refuses, before any script, a schema whose marker names another set, owner or instance
    ///   - a set without an owner still refuses a schema another set has claimed
    /// </remarks>
    public string? SchemaOwner { get; set; }

    /// <summary>Gets or sets the instance this host claims <see cref="SchemaName"/> as, such as <c>identity-haven@prod</c>, so two instances never share one schema.</summary>
    /// <remarks>
    ///   - null at registration derives it: configuration <see cref="MigrationConstants.OwnerInstanceConfigurationKey"/>, else <c>{applicationName}@{DEPLOY_ENVIRONMENT}</c> (<c>@local</c> when not deployed)
    ///   - replicas of one instance share the value; a deploy step that migrates for a host must claim under the host's value
    ///   - matters only with <see cref="SchemaOwner"/>
    /// </remarks>
    public string? OwnerInstance { get; set; }

    /// <summary>Gets or sets how the startup level check treats this set when the database is behind, ahead of or drifted from the code.</summary>
    /// <remarks>An owner conflict always fails, whatever the policy; a compatible ahead always boots with a warning.</remarks>
    public MigrationLevelPolicy LevelMismatch { get; set; } = MigrationLevelPolicy.Fail;

    /// <summary>Gets the sets this set references and the level each must reach first; add through <see cref="DependsOn"/>.</summary>
    public IList<MigrationDependency> Dependencies { get; } = new List<MigrationDependency>();

    /// <summary>Gets the tables this set publishes to other owners; add through <see cref="AddReferenceableTable"/>.</summary>
    public IList<MigrationReferenceableTable> ReferenceableTables { get; } = new List<MigrationReferenceableTable>();

    /// <summary>Gets the extensions the database must already have, such as <c>postgis</c>; apply and check refuse before any SQL while one is missing.</summary>
    /// <remarks>List only untrusted extensions placement creates as superuser; a set creates trusted ones (<c>pg_trgm</c>) itself.</remarks>
    public IList<string> RequiredExtensions { get; } = new List<string>();

    /// <summary>Declares that this set references another set, which must reach <paramref name="level"/> before this set applies.</summary>
    /// <param name="setName">The referenced set, such as <c>identity</c>.</param>
    /// <param name="level">The lowest applied level of the referenced set this set needs.</param>
    /// <returns>These options, for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDatabaseBespokeMigrationSet("orders", typeof(OrdersModule).Assembly, options => options.DependsOn("identity", 3));
    /// </code>
    /// </example>
    public MigrationOptions DependsOn(string setName, int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setName);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);

        Dependencies.Add(new MigrationDependency(setName, level));
        return this;
    }

    /// <summary>Publishes a table other owners may key into and read.</summary>
    /// <param name="table">The table name, snake_case, in <see cref="SchemaName"/>.</param>
    /// <param name="keys">The columns another set may reference.</param>
    /// <param name="readableColumns">The columns another set may read; null publishes the keys only.</param>
    /// <returns>These options, for chaining.</returns>
    public MigrationOptions AddReferenceableTable(string table, IEnumerable<string> keys, IEnumerable<string>? readableColumns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(keys);

        ReferenceableTables.Add(new MigrationReferenceableTable(table, keys.ToList(), readableColumns?.ToList() ?? []));
        return this;
    }

    /// <summary>Requires an extension the database must have before this set applies.</summary>
    /// <param name="extension">The extension name, such as <c>postgis</c>.</param>
    /// <returns>These options, for chaining.</returns>
    public MigrationOptions RequireExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        RequiredExtensions.Add(extension);
        return this;
    }
}
