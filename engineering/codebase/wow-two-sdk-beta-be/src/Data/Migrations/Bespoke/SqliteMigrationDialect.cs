using System.Data;
using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Provides the SQLite migration dialect for file-create-on-open, busy-timeout serialization, and history-table DDL.</summary>
public sealed class SqliteMigrationDialect : IMigrationDialect
{
    private const int BusyTimeoutMilliseconds = 30_000;

    static SqliteMigrationDialect() => SqlMapper.AddTypeHandler(new DateTimeOffsetTextHandler());

    /// <inheritdoc />
    public MigrationCoordinationMode CoordinationMode => MigrationCoordinationMode.SingleApplicantRequired;

    /// <inheritdoc />
    public Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || builder.Mode == SqliteOpenMode.Memory
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        var fullPath = Path.GetFullPath(dataSource);
        var existedBefore = File.Exists(fullPath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        return Task.FromResult(!existedBefore);
    }

    /// <inheritdoc />
    /// <remarks>SQLite has no advisory locks: the busy timeout waits out transient writers, and the deployment keeps one applicant.</remarks>
    public async Task<bool> TryAcquireLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default)
    {
        await connection.ExecuteAsync(new CommandDefinition($"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};", cancellationToken: ct));
        return true;
    }

    /// <inheritdoc />
    public Task ReleaseLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>A table from an older SDK gains <c>kind</c> and <c>contract_since</c>; SQLite's <c>INTEGER</c> already holds 64 bits.</remarks>
    public async Task EnsureHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var table = QualifyHistoryTable(schemaName, tableName);
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                ordinal         INTEGER NOT NULL,
                version         TEXT    NOT NULL,
                name            TEXT    NOT NULL,
                checksum        TEXT    NOT NULL,
                applied_at      TEXT    NOT NULL,
                applied_by      TEXT    NOT NULL,
                execution_ms    INTEGER NOT NULL,
                kind            TEXT    NULL,
                contract_since  TEXT    NULL,
                CONSTRAINT pk_{tableName} PRIMARY KEY (ordinal)
            );
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));

        var columns = await GetColumnTypesAsync(connection, transaction, schemaName, tableName, ct);
        foreach (var column in (string[])["kind", "contract_since"])
        {
            if (!columns.ContainsKey(column))
                await connection.ExecuteAsync(new CommandDefinition($"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL", transaction: transaction, cancellationToken: ct));
        }
    }

    /// <inheritdoc />
    public Task EnsureEventsTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {QualifyHistoryTable(schemaName, tableName)} (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                verb         TEXT    NOT NULL,
                ordinals     TEXT    NOT NULL,
                recorded_by  TEXT    NOT NULL,
                job          TEXT    NULL,
                recorded_at  TEXT    NOT NULL,
                detail       TEXT    NULL
            );
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public Task EnsureSeedHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {QualifyHistoryTable(schemaName, tableName)} (
                name        TEXT NOT NULL,
                checksum    TEXT NOT NULL,
                mode        TEXT NOT NULL,
                applied_at  TEXT NOT NULL,
                applied_by  TEXT NOT NULL,
                CONSTRAINT pk_{tableName} PRIMARY KEY (name)
            );
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetColumnTypesAsync(
        DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        var rows = await connection.QueryAsync<(string Name, string Type)>(new CommandDefinition(
            "SELECT name, type FROM pragma_table_info(@tableName)", new { tableName }, transaction, cancellationToken: ct));
        return rows.ToDictionary(row => row.Name, row => row.Type, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindTableSchemasAsync(DbConnection connection, DbTransaction? transaction, string tableName, CancellationToken ct = default) =>
        await TableExistsAsync(connection, transaction, "main", tableName, ct) ? ["main"] : [];

    /// <inheritdoc />
    /// <remarks><c>SQLITE_BUSY</c> and <c>SQLITE_LOCKED</c> mean another writer held the database past the busy timeout.</remarks>
    public bool IsLockNotAvailable(DbException exception) =>
        exception is SqliteException { SqliteErrorCode: 5 or 6 };

    /// <inheritdoc />
    public string QualifyHistoryTable(string schemaName, string tableName) => $"\"{tableName}\"";

    /// <inheritdoc />
    /// <remarks>SQLite has no schemas, so there is nothing to create.</remarks>
    public Task EnsureSchemaAsync(DbConnection connection, DbTransaction? transaction, string schemaName, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public async Task<bool> TableExistsAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default) =>
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName",
            new { tableName }, transaction, cancellationToken: ct)) > 0;

    /// <inheritdoc />
    /// <remarks>
    ///   - <c>{{schema}}.</c> becomes nothing, so <c>{{schema}}.identity_users</c> reads <c>identity_users</c>; table prefixes keep sets apart
    ///   - a bare <c>{{schema}}</c> becomes <c>main</c>, SQLite's name for the database's own schema
    /// </remarks>
    public string SubstituteSchema(string sql, string schemaName) =>
        sql.Replace(MigrationConstants.SchemaPlaceholder + ".", "", StringComparison.Ordinal)
            .Replace(MigrationConstants.SchemaPlaceholder, "main", StringComparison.Ordinal);

    /// <inheritdoc />
    /// <remarks>Like <see cref="SubstituteSchema"/>: <c>{{schema:set}}.</c> becomes nothing and a bare placeholder <c>main</c>.</remarks>
    public string SubstituteSetSchema(string sql, string placeholder, string schemaName) =>
        sql.Replace(placeholder + ".", "", StringComparison.Ordinal)
            .Replace(placeholder, "main", StringComparison.Ordinal);

    /// <summary>Round-trips <see cref="DateTimeOffset"/> through SQLite TEXT as ISO 8601.</summary>
    private sealed class DateTimeOffsetTextHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        /// <inheritdoc />
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(dateTime, TimeSpan.Zero),
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name ?? "null"} to DateTimeOffset."),
        };

        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
    }
}
