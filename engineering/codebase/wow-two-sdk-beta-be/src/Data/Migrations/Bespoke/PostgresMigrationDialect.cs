using System.Data.Common;
using System.Globalization;
using Dapper;
using Npgsql;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Provides the PostgreSQL migration dialect for maintenance-database and schema create, advisory locking, and history-table DDL.</summary>
public sealed class PostgresMigrationDialect : IMigrationDialect
{
    private const string MaintenanceDatabase = "postgres";

    /// <summary>The SQLSTATE PostgreSQL raises when <c>lock_timeout</c> expires: <c>lock_not_available</c>.</summary>
    private const string LockNotAvailable = "55P03";

    /// <summary>The longest query text a lock holder reports.</summary>
    private const int QueryPreviewLength = 200;

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
    /// <remarks>The reserved key takes the two-key form <c>pg_try_advisory_lock(int, int)</c>; the legacy key the single bigint form.</remarks>
    public Task<bool> TryAcquireLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default) =>
        key.KeyClass is { } keyClass
            ? connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_try_advisory_lock(@keyClass, @key)", new { keyClass, key = (int)key.Key }, cancellationToken: ct))
            : connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_try_advisory_lock(@key)", new { key = key.Key }, cancellationToken: ct));

    /// <inheritdoc />
    public Task ReleaseLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default) =>
        key.KeyClass is { } keyClass
            ? connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_unlock(@keyClass, @key)", new { keyClass, key = (int)key.Key }, cancellationToken: ct))
            : connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_unlock(@key)", new { key = key.Key }, cancellationToken: ct));

    /// <inheritdoc />
    /// <remarks>
    ///   - a table from an older SDK gains <c>kind</c> and <c>contract_since</c>, and <c>execution_ms</c> widens to <c>bigint</c>
    ///   - each change runs only when the catalog shows it missing, so a current table takes no lock beyond the create check
    /// </remarks>
    public async Task EnsureHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var table = QualifyHistoryTable(schemaName, tableName);
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                ordinal         integer      NOT NULL,
                version         varchar(20)  NOT NULL,
                name            varchar(120) NOT NULL,
                checksum        char(64)     NOT NULL,
                applied_at      timestamptz  NOT NULL,
                applied_by      varchar(60)  NOT NULL,
                execution_ms    bigint       NOT NULL,
                kind            varchar(10)  NULL,
                contract_since  varchar(20)  NULL,
                CONSTRAINT pk_{tableName} PRIMARY KEY (ordinal),
                CONSTRAINT ck_{tableName}_kind CHECK (kind IN ('expand', 'contract'))
            );
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));

        var columns = await GetColumnTypesAsync(connection, transaction, schemaName, tableName, ct);
        var upgrades = new List<string>();
        if (!columns.ContainsKey("kind"))
            upgrades.Add($"ADD COLUMN kind varchar(10) NULL CONSTRAINT ck_{tableName}_kind CHECK (kind IN ('expand', 'contract'))");
        if (!columns.ContainsKey("contract_since"))
            upgrades.Add("ADD COLUMN contract_since varchar(20) NULL");
        if (columns.TryGetValue("execution_ms", out var executionType) && executionType == "integer")
            upgrades.Add("ALTER COLUMN execution_ms TYPE bigint");

        if (upgrades.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"ALTER TABLE {table} {string.Join(", ", upgrades)}", transaction: transaction, cancellationToken: ct));
        }
    }

    /// <inheritdoc />
    public Task EnsureEventsTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {QualifyHistoryTable(schemaName, tableName)} (
                id           bigint       GENERATED ALWAYS AS IDENTITY,
                verb         varchar(20)  NOT NULL,
                ordinals     text         NOT NULL,
                recorded_by  varchar(120) NOT NULL,
                job          varchar(120) NULL,
                recorded_at  timestamptz  NOT NULL,
                detail       text         NULL,
                CONSTRAINT pk_{tableName} PRIMARY KEY (id)
            );
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public Task EnsureSeedHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {QualifyHistoryTable(schemaName, tableName)} (
                name        varchar(120) NOT NULL,
                checksum    char(64)     NOT NULL,
                mode        varchar(10)  NOT NULL,
                applied_at  timestamptz  NOT NULL,
                applied_by  varchar(60)  NOT NULL,
                CONSTRAINT pk_{tableName} PRIMARY KEY (name)
            );
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public string QualifyHistoryTable(string schemaName, string tableName) => $"\"{schemaName}\".\"{tableName}\"";

    /// <inheritdoc />
    /// <remarks>The type is the catalog's own <c>format_type</c> text, so it is safe to write into the statement.</remarks>
    public string CastParameter(string parameter, string? columnType) =>
        columnType is null ? parameter : $"CAST({parameter} AS {columnType})";

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
    public async Task<IReadOnlyDictionary<string, string>> GetColumnTypesAsync(
        DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var rows = await connection.QueryAsync<(string Name, string Type)>(new CommandDefinition(
            """
            SELECT a.attname::text, format_type(a.atttypid, a.atttypmod)
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid = to_regclass(@qualified) AND a.attnum > 0 AND NOT a.attisdropped
            """,
            new { qualified = QualifyHistoryTable(schemaName, tableName) }, transaction, cancellationToken: ct));
        return rows.ToDictionary(row => row.Name, row => row.Type, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindTableSchemasAsync(DbConnection connection, DbTransaction? transaction, string tableName, CancellationToken ct = default)
    {
        var schemas = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT schemaname::text FROM pg_catalog.pg_tables WHERE tablename = @tableName ORDER BY schemaname",
            new { tableName }, transaction, cancellationToken: ct));
        return schemas.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>?> GetInstalledExtensionsAsync(DbConnection connection, CancellationToken ct = default)
    {
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT extname::text FROM pg_catalog.pg_extension", cancellationToken: ct));
        return names.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    /// <remarks><c>SET</c> takes no parameters, so the bound is written as whole milliseconds.</remarks>
    public Task SetLockTimeoutAsync(DbConnection connection, DbTransaction? transaction, TimeSpan timeout, CancellationToken ct = default)
    {
        var milliseconds = ((long)Math.Max(1, timeout.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture);
        var scope = transaction is null ? "SET" : "SET LOCAL";
        return connection.ExecuteAsync(new CommandDefinition(
            $"{scope} lock_timeout = '{milliseconds}ms'", transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public Task ResetLockTimeoutAsync(DbConnection connection, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition("RESET lock_timeout", cancellationToken: ct));

    /// <inheritdoc />
    public bool IsLockNotAvailable(DbException exception) =>
        exception is PostgresException { SqlState: LockNotAvailable };

    /// <inheritdoc />
    /// <remarks>
    ///   - <c>pg_locks</c> spells a two-key lock as <c>classid</c>/<c>objid</c> with <c>objsubid = 2</c>, a bigint key as its high and low words with <c>objsubid = 1</c>
    ///   - a role sees another role's query only with <c>pg_read_all_stats</c>
    /// </remarks>
    public async Task<IReadOnlyList<MigrationLockHolder>> GetLockHoldersAsync(
        DbConnection connection, IReadOnlyList<MigrationLockKey> keys, CancellationToken ct = default)
    {
        var holders = new List<MigrationLockHolder>();
        foreach (var key in keys)
        {
            var (classId, objectId, subId) = key.KeyClass is { } keyClass
                ? ((long)(uint)keyClass, (long)(uint)(int)key.Key, 2)
                : ((long)(uint)(key.Key >>> 32), (long)(uint)key.Key, 1);

            var rows = await connection.QueryAsync<(int Pid, string? ApplicationName, DateTime? Since, string? Query)>(new CommandDefinition(
                """
                SELECT l.pid, a.application_name, COALESCE(a.xact_start, a.backend_start), a.query
                FROM pg_catalog.pg_locks l
                LEFT JOIN pg_catalog.pg_stat_activity a ON a.pid = l.pid
                WHERE l.locktype = 'advisory'
                  AND l.granted
                  AND l.database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                  AND l.classid::bigint = @classId
                  AND l.objid::bigint = @objectId
                  AND l.objsubid = @subId
                  AND l.pid <> pg_backend_pid()
                ORDER BY l.pid
                """,
                new { classId, objectId, subId }, cancellationToken: ct));

            holders.AddRange(rows.Select(row => new MigrationLockHolder
            {
                Pid = row.Pid,
                ApplicationName = string.IsNullOrEmpty(row.ApplicationName) ? null : row.ApplicationName,
                Since = row.Since is { } since ? new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)) : null,
                Query = row.Query is { Length: > QueryPreviewLength } query ? query[..QueryPreviewLength] : row.Query,
                Key = key.IsLegacy ? "legacy" : "reserved",
            }));
        }

        return holders;
    }

    /// <inheritdoc />
    public Task<bool> TerminateSessionAsync(DbConnection connection, int pid, CancellationToken ct = default) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT pg_terminate_backend(@pid)", new { pid }, cancellationToken: ct));

    /// <inheritdoc />
    /// <remarks>Writes the bare name, so the placeholder works as an identifier and inside a string literal alike.</remarks>
    public string SubstituteSchema(string sql, string schemaName) =>
        sql.Replace(MigrationConstants.SchemaPlaceholder, schemaName, StringComparison.Ordinal);
}
