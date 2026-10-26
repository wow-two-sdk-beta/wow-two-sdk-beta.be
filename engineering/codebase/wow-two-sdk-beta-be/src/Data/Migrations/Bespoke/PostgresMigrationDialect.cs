using System.Data.Common;
using Dapper;
using Npgsql;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Provides the PostgreSQL migration dialect for maintenance-database and schema create, advisory locking, and history-table DDL.</summary>
public sealed class PostgresMigrationDialect : IMigrationDialect
{
    private const string MaintenanceDatabase = "postgres";

    /// <inheritdoc />
    public MigrationCoordinationMode CoordinationMode => MigrationCoordinationMode.DatabaseLock;

    /// <inheritdoc />
    public async Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
            return false;

        // Switch to the maintenance database — the target may not exist yet.
        builder.Database = MaintenanceDatabase;

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);

        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        check.Parameters.AddWithValue("name", databaseName);
        if (await check.ExecuteScalarAsync(ct) is not null)
            return false;

        // CREATE DATABASE cannot be parameterized; the name comes from config, not user input.
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await create.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public Task AcquireLockAsync(DbConnection connection, long lockId, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_lock(@id)", new { id = lockId }, cancellationToken: ct));

    /// <inheritdoc />
    public Task ReleaseLockAsync(DbConnection connection, long lockId, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_unlock(@id)", new { id = lockId }, cancellationToken: ct));

    /// <inheritdoc />
    public Task EnsureHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {QualifyHistoryTable(schemaName, tableName)} (
                ordinal       integer      NOT NULL,
                version       varchar(20)  NOT NULL,
                name          varchar(120) NOT NULL,
                checksum      char(64)     NOT NULL,
                applied_at    timestamptz  NOT NULL,
                applied_by    varchar(60)  NOT NULL,
                execution_ms  integer      NOT NULL,
                CONSTRAINT pk_{tableName} PRIMARY KEY (ordinal)
            );
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public string QualifyHistoryTable(string schemaName, string tableName) => $"\"{schemaName}\".\"{tableName}\"";

    /// <inheritdoc />
    /// <remarks>Checks the catalog first, so an existing schema needs no <c>CREATE</c> privilege on the database.</remarks>
    public async Task EnsureSchemaAsync(DbConnection connection, DbTransaction? transaction, string schemaName, CancellationToken ct = default)
    {
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = @schemaName)",
            new { schemaName }, transaction, cancellationToken: ct));
        if (exists)
            return;

        // CREATE SCHEMA cannot be parameterized; the name comes from configuration, not user input.
        await connection.ExecuteAsync(new CommandDefinition(
            $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"", transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public Task<bool> TableExistsAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname = @schemaName AND tablename = @tableName)",
            new { schemaName, tableName }, transaction, cancellationToken: ct));

    /// <inheritdoc />
    /// <remarks>Writes the bare name, so the placeholder works as an identifier and inside a string literal alike.</remarks>
    public string SubstituteSchema(string sql, string schemaName) =>
        sql.Replace(MigrationConstants.SchemaPlaceholder, schemaName, StringComparison.Ordinal);
}
