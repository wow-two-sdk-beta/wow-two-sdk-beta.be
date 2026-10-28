using System.Data.Common;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.Parsers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>One configured bespoke migrator over a real database — the DI graph the engine runs inside, plus direct-SQL assertion helpers, across Postgres and SQLite.</summary>
/// <remarks>
///   - the single-set factories register the host's default set
///   - the <see cref="MigratorSetsBuilder"/> factories register several named sets, such as a module's beside a host's
/// </remarks>
public sealed class MigratorHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DatabaseProvider _databaseProvider;
    private readonly IAsyncDisposable? _ownedDataSource;

    private MigratorHarness(ServiceProvider provider, DatabaseProvider databaseProvider, IAsyncDisposable? ownedDataSource)
    {
        _provider = provider;
        _databaseProvider = databaseProvider;
        _ownedDataSource = ownedDataSource;
    }

    /// <summary>The default set's runner under test.</summary>
    /// <exception cref="InvalidOperationException">The harness registered no default set.</exception>
    public IMigrationRunnerService Runner => _provider.GetRequiredService<IMigrationRunnerService>();

    /// <summary>The default set's effective options, after the configure hook.</summary>
    /// <exception cref="InvalidOperationException">The harness registered no default set.</exception>
    public MigrationOptions Options => _provider.GetRequiredService<MigrationOptions>();

    /// <summary>The root provider, which the multi-set helpers such as <c>CheckMigrationLevelsAsync</c> and <c>VerifyMigrationLevelsOnStartupAsync</c> extend.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Every registered set, in registration order.</summary>
    public IMigrationSetRegistry Sets => _provider.GetRequiredService<IMigrationSetRegistry>();

    /// <summary>Gets one set's runner.</summary>
    /// <param name="setName">The set name; <see cref="MigrationConstants.DefaultSetName"/> is the host's set.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    public IMigrationRunnerService RunnerFor(string setName) => Sets.GetRunner(setName);

    /// <summary>Gets one set's effective options.</summary>
    /// <param name="setName">The set name.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    public MigrationOptions OptionsFor(string setName) => Sets.GetOptions(setName);

    /// <summary>The target provider's simultaneous-applicant guarantee.</summary>
    public MigrationCoordinationMode CoordinationMode =>
        _provider.GetRequiredService<IMigrationDialect>().CoordinationMode;

    /// <summary>Builds a Postgres migrator over <paramref name="connectionString"/> reading filesystem migrations from <paramref name="migrationsRoot"/>.</summary>
    /// <param name="connectionString">The Postgres connection string, typically a container DB.</param>
    /// <param name="migrationsRoot">The on-disk <c>NNN-name</c> migrations root, typically a <see cref="MigrationsWorkspace.Root"/>.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/>.</param>
    public static MigratorHarness CreatePostgres(string connectionString, string migrationsRoot, Action<MigrationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentException.ThrowIfNullOrEmpty(migrationsRoot);

        return BuildPostgres(connectionString, services => services.AddDatabaseBespokeMigrations(migrationsRoot, Provider(DatabaseProvider.Postgres, configure)));
    }

    /// <summary>Builds a Postgres migrator over <paramref name="connectionString"/> reading embedded migrations from <paramref name="sqlAssembly"/>.</summary>
    /// <param name="connectionString">The Postgres connection string, typically a container DB.</param>
    /// <param name="sqlAssembly">The assembly embedding the real <c>Migrations/NNN-name/*.sql</c> resources.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/>.</param>
    public static MigratorHarness CreatePostgres(string connectionString, Assembly sqlAssembly, Action<MigrationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(sqlAssembly);

        return BuildPostgres(connectionString, services => services.AddDatabaseBespokeMigrations(sqlAssembly, Provider(DatabaseProvider.Postgres, configure)));
    }

    /// <summary>Builds a SQLite migrator over <paramref name="connectionString"/> reading filesystem migrations from <paramref name="migrationsRoot"/>.</summary>
    /// <param name="connectionString">The SQLite connection string, e.g. a temp file.</param>
    /// <param name="migrationsRoot">The on-disk <c>NNN-name</c> migrations root, typically a <see cref="MigrationsWorkspace.Root"/>.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/>.</param>
    public static MigratorHarness CreateSqlite(string connectionString, string migrationsRoot, Action<MigrationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentException.ThrowIfNullOrEmpty(migrationsRoot);

        return BuildSqlite(connectionString, services => services.AddDatabaseBespokeMigrations(migrationsRoot, Provider(DatabaseProvider.Sqlite, configure)));
    }

    /// <summary>Builds a SQLite migrator over <paramref name="connectionString"/> reading embedded migrations from <paramref name="sqlAssembly"/>.</summary>
    /// <param name="connectionString">The SQLite connection string, e.g. a temp file.</param>
    /// <param name="sqlAssembly">The assembly embedding the real <c>Migrations/NNN-name/*.sql</c> resources.</param>
    /// <param name="configure">An optional hook to override <see cref="MigrationOptions"/>.</param>
    public static MigratorHarness CreateSqlite(string connectionString, Assembly sqlAssembly, Action<MigrationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(sqlAssembly);

        return BuildSqlite(connectionString, services => services.AddDatabaseBespokeMigrations(sqlAssembly, Provider(DatabaseProvider.Sqlite, configure)));
    }

    /// <summary>Builds a Postgres harness over <paramref name="connectionString"/> with the sets <paramref name="sets"/> adds, in its order.</summary>
    /// <param name="connectionString">The Postgres connection string, typically a container DB.</param>
    /// <param name="sets">Adds the default set and each named set; every set runs the Postgres dialect.</param>
    /// <example>
    /// <code>
    /// await using var migrator = MigratorHarness.CreatePostgres(connectionString, sets => sets
    ///     .AddDefault(workspace.Root)
    ///     .Add("identity", typeof(IdentityModule).Assembly, options => options.SchemaName = "identity"));
    /// await migrator.ApplyAllAsync();
    /// </code>
    /// </example>
    public static MigratorHarness CreatePostgres(string connectionString, Action<MigratorSetsBuilder> sets)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(sets);

        return BuildPostgres(connectionString, services => MigratorSetsBuilder.Register(services, DatabaseProvider.Postgres, sets));
    }

    /// <summary>Builds a SQLite harness over <paramref name="connectionString"/> with the sets <paramref name="sets"/> adds, in its order.</summary>
    /// <param name="connectionString">The SQLite connection string, e.g. a temp file.</param>
    /// <param name="sets">Adds the default set and each named set; every set runs the SQLite dialect.</param>
    public static MigratorHarness CreateSqlite(string connectionString, Action<MigratorSetsBuilder> sets)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(sets);

        return BuildSqlite(connectionString, services => MigratorSetsBuilder.Register(services, DatabaseProvider.Sqlite, sets));
    }

    /// <summary>Applies every set's pending migrations in registration order, the way a deploy step does.</summary>
    /// <param name="appliedBy">The host stamp recorded on each applied row.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Each set's applied labels, or the first set's failure.</returns>
    public Task<Result<IReadOnlyList<MigrationSetApplied>>> ApplyAllAsync(string appliedBy = "test", CancellationToken cancellationToken = default) =>
        _provider.ApplyMigrationSetsAsync(appliedBy, setNames: null, cancellationToken);

    /// <summary>Proves a set's scripts idempotent with history bypassed: Apply → Apply, Rollback → Rollback, Apply → Rollback → Apply (M59).</summary>
    /// <remarks>
    ///   - runs every <c>Apply.sql</c> and <c>Rollback.sql</c> directly, twice each, and compares the database after each pair; touches no history
    ///   - run it on an empty database, such as a fresh <see cref="MigratorPostgresFixture"/> reset; it leaves the set applied
    /// </remarks>
    /// <param name="setName">The set; <see cref="MigrationConstants.DefaultSetName"/> is the host's own.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The migrations proven, or the first failed step.</returns>
    public Task<MigrationIdempotencyReport> CheckIdempotencyAsync(string setName = MigrationConstants.DefaultSetName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(setName);

        var proof = new MigrationIdempotencyProof(Sets, _provider.GetRequiredService<ISqlStatementParser>(), OpenConnectionAsync);
        return proof.RunAsync(setName, cancellationToken);
    }

    /// <summary>Proves a set's scripts idempotent like <see cref="CheckIdempotencyAsync"/>, in one call a migrations tier makes.</summary>
    /// <param name="setName">The set; <see cref="MigrationConstants.DefaultSetName"/> is the host's own.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="MigrationIdempotencyException">A script failed or changed the database when it ran again.</exception>
    /// <example>
    /// <code>
    /// await using var migrator = MigratorHarness.CreatePostgres(fixture.ConnectionString, typeof(AppDbContext).Assembly);
    /// await migrator.AssertIdempotentAsync();
    /// </code>
    /// </example>
    public async Task AssertIdempotentAsync(string setName = MigrationConstants.DefaultSetName, CancellationToken cancellationToken = default)
    {
        var report = await CheckIdempotencyAsync(setName, cancellationToken).ConfigureAwait(false);
        if (!report.Passed)
            throw new MigrationIdempotencyException(report.ToString());
    }

    /// <summary>Opens a connection via the SDK factory the runner uses, for direct database assertions.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        _provider.GetRequiredService<IDbConnectionFactory>().CreateOpenAsync(cancellationToken);

    /// <summary>Reads every history row of the default set ordered by ordinal — the source of truth for recording assertions.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public Task<IReadOnlyList<MigrationHistoryRow>> ReadHistoryAsync(CancellationToken cancellationToken = default) =>
        ReadHistoryAsync(MigrationConstants.DefaultSetName, cancellationToken);

    /// <summary>Reads every history row of one set ordered by ordinal, from the set's own history table.</summary>
    /// <param name="setName">The set name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    public async Task<IReadOnlyList<MigrationHistoryRow>> ReadHistoryAsync(string setName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(setName);

        var options = Sets.GetOptions(setName);
        var table = Sets.GetDialect(setName).QualifyHistoryTable(options.SchemaName, options.TableName);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MigrationHistoryRow>(
            $"select ordinal, name, checksum, applied_by as AppliedBy from {table} order by ordinal").ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>Returns whether a table exists in the database, across the Postgres and SQLite catalogs.</summary>
    /// <param name="table">The table name to probe.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public Task<bool> HasTableAsync(string table, CancellationToken cancellationToken = default) =>
        HasTableAsync("public", table, cancellationToken);

    /// <summary>Returns whether a table exists in a schema; SQLite has no schemas and ignores <paramref name="schema"/>.</summary>
    /// <param name="schema">The Postgres schema to probe, such as a module set's.</param>
    /// <param name="table">The table name to probe.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<bool> HasTableAsync(string schema, string table, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(schema);
        ArgumentException.ThrowIfNullOrEmpty(table);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return _databaseProvider == DatabaseProvider.Sqlite
            ? await connection.ExecuteScalarAsync<long>(
                "select count(*) from sqlite_master where type = 'table' and name = @table", new { table }).ConfigureAwait(false) > 0
            : await connection.ExecuteScalarAsync<bool>(
                "select exists (select 1 from information_schema.tables where table_schema = @schema and table_name = @table)",
                new { schema, table }).ConfigureAwait(false);
    }

    /// <summary>Returns whether an index exists in the database, across the Postgres and SQLite catalogs.</summary>
    /// <param name="index">The index name to probe.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<bool> HasIndexAsync(string index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return _databaseProvider == DatabaseProvider.Sqlite
            ? await connection.ExecuteScalarAsync<long>(
                "select count(*) from sqlite_master where type = 'index' and name = @index", new { index }).ConfigureAwait(false) > 0
            : await connection.ExecuteScalarAsync<bool>(
                "select exists (select 1 from pg_indexes where schemaname = 'public' and indexname = @index)", new { index }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        if (_ownedDataSource is not null)
            await _ownedDataSource.DisposeAsync().ConfigureAwait(false);
    }

    private static MigratorHarness BuildPostgres(string connectionString, Action<IServiceCollection> addMigrations)
    {
        var services = NewServices();

        // Postgres exposes a DbDataSource — the seam the SDK's DataSourceConnectionFactory binds to (the production path).
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton<DbDataSource>(dataSource);
        services.AddDataSourceConnectionFactory();
        addMigrations(services);

        // The provider does not own an instance registered via AddSingleton(instance), so the harness disposes it.
        return new MigratorHarness(services.BuildServiceProvider(), DatabaseProvider.Postgres, dataSource);
    }

    private static MigratorHarness BuildSqlite(string connectionString, Action<IServiceCollection> addMigrations)
    {
        var services = NewServices();

        // SQLite has no DbDataSource, so the dedicated SQLite factory is the seam (the production adoption path).
        services.AddSqliteConnectionFactory(connectionString);
        addMigrations(services);

        return new MigratorHarness(services.BuildServiceProvider(), DatabaseProvider.Sqlite, ownedDataSource: null);
    }

    private static ServiceCollection NewServices()
    {
        // Dapper maps snake_case history columns (applied_at to AppliedAt) — the convention the product sets.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSimpleConsole());
        return services;
    }

    private static Action<MigrationOptions> Provider(DatabaseProvider provider, Action<MigrationOptions>? configure) =>
        options =>
        {
            options.Provider = provider;
            configure?.Invoke(options);
        };
}
