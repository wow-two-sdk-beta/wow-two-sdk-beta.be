using Npgsql;
using Testcontainers.PostgreSql;
using WoW.Two.Sdk.Beta.Be.Testing.Containers;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Owns one Postgres container for a migrator suite; <see cref="ResetAsync"/> drops every user schema and recreates <c>public</c> so each test applies from scratch.</summary>
/// <remarks>The deliberate opposite of the base <c>PostgresFixture</c>, whose Respawn reset preserves <c>migration_history</c>; migrator tests need the full drop for apply-from-scratch, drift, and rollback. Implements xUnit <see cref="IAsyncLifetime"/> so it serves directly as an <c>ICollectionFixture</c>.</remarks>
public sealed class MigratorPostgresFixture : ContainerFixtureBase<PostgreSqlContainer>, IAsyncLifetime
{
    // A single public constructor — xUnit collection fixtures require exactly one, and the image is pinned so a suite never floats across engine versions.
    /// <summary>Creates a fixture over a pinned Postgres image.</summary>
    public MigratorPostgresFixture() : base(new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build())
    {
    }

    /// <inheritdoc />
    public override string Name => "postgres-migrator";

    /// <summary>The connection string of the started container.</summary>
    public string ConnectionString => Container.GetConnectionString();

    /// <summary>Drops every user schema — a module set's included — and recreates <c>public</c>, wiping every object and every set's history.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public override async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DO $$
            DECLARE user_schema text;
            BEGIN
                FOR user_schema IN
                    SELECT nspname FROM pg_catalog.pg_namespace
                    WHERE nspname <> 'information_schema' AND nspname NOT LIKE 'pg\_%'
                LOOP
                    EXECUTE format('DROP SCHEMA %I CASCADE', user_schema);
                END LOOP;
            END $$;
            CREATE SCHEMA public;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the container for xUnit, so the fixture serves directly as an <c>ICollectionFixture</c>.</summary>
    Task IAsyncLifetime.InitializeAsync() => StartAsync().AsTask();

    /// <summary>Disposes the container for xUnit's <see cref="IAsyncLifetime"/> contract.</summary>
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();
}
