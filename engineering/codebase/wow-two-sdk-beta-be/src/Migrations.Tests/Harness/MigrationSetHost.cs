using System.Data.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

/// <summary>A host that registers several migration sets over one database, the way a product plus a module would.</summary>
public sealed class MigrationSetHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DatabaseProvider _databaseProvider;
    private readonly NpgsqlDataSource? _dataSource;

    private MigrationSetHost(ServiceProvider provider, DatabaseProvider databaseProvider, CapturingLoggerProvider logs, NpgsqlDataSource? dataSource)
    {
        _provider = provider;
        _databaseProvider = databaseProvider;
        _dataSource = dataSource;
        Logs = logs;
    }

    /// <summary>The host's root provider, which the set helpers extend.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>The registered sets.</summary>
    public IMigrationSetRegistry Sets => _provider.GetRequiredService<IMigrationSetRegistry>();

    /// <summary>Every log entry the host wrote.</summary>
    public CapturingLoggerProvider Logs { get; }

    /// <summary>Builds a SQLite host; <paramref name="register"/> adds the sets.</summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="register">Registers the migration sets.</param>
    public static MigrationSetHost Sqlite(string connectionString, Action<IServiceCollection> register)
    {
        var (services, logs) = NewServices();
        services.AddSqliteConnectionFactory(connectionString);
        register(services);
        return new MigrationSetHost(services.BuildServiceProvider(), DatabaseProvider.Sqlite, logs, null);
    }

    /// <summary>Builds a PostgreSQL host; <paramref name="register"/> adds the sets.</summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="register">Registers the migration sets.</param>
    public static MigrationSetHost Postgres(string connectionString, Action<IServiceCollection> register)
    {
        var (services, logs) = NewServices();
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton<DbDataSource>(dataSource);
        services.AddDataSourceConnectionFactory();
        register(services);
        return new MigrationSetHost(services.BuildServiceProvider(), DatabaseProvider.Postgres, logs, dataSource);
    }

    /// <summary>Opens a connection through the factory the sets use.</summary>
    public ValueTask<DbConnection> OpenConnectionAsync() =>
        _provider.GetRequiredService<IDbConnectionFactory>().CreateOpenAsync();

    /// <summary>Returns whether a table exists; <paramref name="schema"/> is ignored on SQLite.</summary>
    /// <param name="schema">The PostgreSQL schema to probe.</param>
    /// <param name="table">The table name.</param>
    public async Task<bool> HasTableAsync(string schema, string table)
    {
        await using var connection = await OpenConnectionAsync();
        return _databaseProvider == DatabaseProvider.Sqlite
            ? await connection.ExecuteScalarAsync<long>(
                "select count(*) from sqlite_master where type = 'table' and name = @table", new { table }) > 0
            : await connection.ExecuteScalarAsync<bool>(
                "select exists (select 1 from information_schema.tables where table_schema = @schema and table_name = @table)",
                new { schema, table });
    }

    /// <summary>Returns whether a PostgreSQL schema exists.</summary>
    /// <param name="schema">The schema name.</param>
    public async Task<bool> HasSchemaAsync(string schema)
    {
        await using var connection = await OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "select exists (select 1 from pg_namespace where nspname = @schema)", new { schema });
    }

    /// <summary>Reads the ordinals and names recorded in a history table, by its qualified name.</summary>
    /// <param name="qualifiedTable">The table reference, such as <c>migration_history</c> or <c>"identity"."identity_migration_history"</c>.</param>
    public async Task<IReadOnlyList<string>> ReadHistoryAsync(string qualifiedTable)
    {
        await using var connection = await OpenConnectionAsync();
        var rows = await connection.QueryAsync<(int Ordinal, string Name)>(
            $"select ordinal, name from {qualifiedTable} order by ordinal");
        return rows.Select(row => $"{row.Ordinal:D3}-{row.Name}").ToList();
    }

    /// <summary>Reads the host stamp of every row in a history table, by ordinal.</summary>
    /// <param name="qualifiedTable">The table reference.</param>
    public async Task<IReadOnlyList<string>> ReadAppliedByAsync(string qualifiedTable)
    {
        await using var connection = await OpenConnectionAsync();
        var rows = await connection.QueryAsync<string>($"select applied_by from {qualifiedTable} order by ordinal");
        return rows.ToList();
    }

    /// <summary>Reads every owner marker row.</summary>
    /// <param name="qualifiedTable">The marker table reference.</param>
    public async Task<IReadOnlyList<(string SchemaName, string SetName, string Owner, string Instance, string ClaimedBy)>> ReadOwnersAsync(string qualifiedTable)
    {
        await using var connection = await OpenConnectionAsync();
        var rows = await connection.QueryAsync<(string, string, string, string, string)>(
            $"select schema_name, set_name, owner, instance, claimed_by from {qualifiedTable} order by schema_name");
        return rows.ToList();
    }

    /// <summary>Drops PostgreSQL schemas a test created outside <c>public</c>.</summary>
    /// <param name="schemas">The schema names.</param>
    public async Task DropSchemasAsync(params string[] schemas)
    {
        await using var connection = await OpenConnectionAsync();
        foreach (var schema in schemas)
            await connection.ExecuteAsync($"drop schema if exists \"{schema}\" cascade");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    private static (ServiceCollection Services, CapturingLoggerProvider Logs) NewServices()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        return (services, logs);
    }
}
