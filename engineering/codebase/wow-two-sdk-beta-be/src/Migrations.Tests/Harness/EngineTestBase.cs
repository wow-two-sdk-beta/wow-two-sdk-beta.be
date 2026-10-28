using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

/// <summary>Base for tests of the migrate verbs on PostgreSQL: a reset database per test, hosts over it, and direct-SQL probes.</summary>
/// <param name="fixture">The shared drop-schema Postgres fixture.</param>
public abstract class EngineTestBase(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    /// <summary>Builds a PostgreSQL host; <paramref name="register"/> adds the sets.</summary>
    /// <param name="register">Registers the migration sets.</param>
    protected MigrationSetHost CreateHost(Action<IServiceCollection> register) =>
        MigrationSetHost.Postgres(Fixture.ConnectionString, register);

    /// <summary>Builds a host whose default set reads this test's workspace, deployed unless <paramref name="configure"/> says otherwise.</summary>
    /// <param name="release">The owner release the image carries.</param>
    /// <param name="configure">Further options.</param>
    protected MigrationSetHost CreateDefaultHost(string release = "0.5.0", Action<MigrationOptions>? configure = null) =>
        CreateHost(services => services.AddDatabaseBespokeMigrations(Workspace.Root, options =>
        {
            options.Release = release;
            options.LocalRun = false;
            options.AdvisoryLockWait = TimeSpan.FromSeconds(2);
            configure?.Invoke(options);
        }));

    /// <summary>Gets a host's verb engine.</summary>
    /// <param name="host">The host.</param>
    protected static IMigrationEngine Engine(MigrationSetHost host) => host.Services.GetRequiredService<IMigrationEngine>();

    /// <summary>Opens a raw connection to the test database, outside every host.</summary>
    /// <param name="applicationName">The session's <c>application_name</c>, or null.</param>
    protected async Task<NpgsqlConnection> OpenAsync(string? applicationName = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Pooling = false };
        if (applicationName is not null)
            builder.ApplicationName = applicationName;
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Runs SQL on a fresh connection.</summary>
    /// <param name="sql">The SQL.</param>
    /// <param name="parameters">The parameters, or null.</param>
    protected async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>Reads one value on a fresh connection.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="sql">The SQL.</param>
    /// <param name="parameters">The parameters, or null.</param>
    protected async Task<T?> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    /// <summary>Returns whether a table exists.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="table">The table.</param>
    protected Task<bool> TableExistsAsync(string schema, string table) => ScalarAsync<bool>(
        "select exists (select 1 from information_schema.tables where table_schema = @schema and table_name = @table)", new { schema, table });

    /// <summary>Reads a set's event rows: verb and ordinals, oldest first.</summary>
    /// <param name="qualifiedTable">The event table, such as <c>public.migration_history_events</c>.</param>
    protected async Task<IReadOnlyList<(string Verb, string Ordinals)>> ReadEventsAsync(string qualifiedTable = "public.migration_history_events")
    {
        await using var connection = await OpenAsync();
        return (await connection.QueryAsync<(string, string)>($"select verb, ordinals from {qualifiedTable} order by id")).ToList();
    }
}
