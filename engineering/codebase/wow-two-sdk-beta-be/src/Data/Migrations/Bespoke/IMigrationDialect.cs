using System.Data.Common;
using Dapper;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines provider-specific migration SQL: database and schema creation, locking, history DDL and schema substitution.</summary>
/// <remarks>Members with a default body are optional for a provider; the defaults keep a provider without the feature working.</remarks>
public interface IMigrationDialect
{
    /// <summary>Gets the provider's simultaneous-applicant guarantee.</summary>
    MigrationCoordinationMode CoordinationMode { get; }

    /// <summary>Creates the target database if it is missing, returning whether a create occurred.</summary>
    /// <param name="connectionString">The connection string whose database name is ensured.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default);

    /// <summary>Tries once to take one advisory-lock key for the session, without waiting.</summary>
    /// <param name="connection">The open connection that holds the lock for its session.</param>
    /// <param name="key">The key to take.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Whether the session now holds the key.</returns>
    Task<bool> TryAcquireLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default);

    /// <summary>Releases one advisory-lock key the session holds.</summary>
    /// <param name="connection">The connection that holds the lock.</param>
    /// <param name="key">The key to release.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ReleaseLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default);

    /// <summary>Creates the migration-history table if it does not exist, and adds the columns a table from an older SDK lacks.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, such as a dry run's, or null to run bare.</param>
    /// <param name="schemaName">The schema that owns the history table.</param>
    /// <param name="tableName">The history table name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task EnsureHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default);

    /// <summary>Creates a set's append-only event table if it does not exist.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, or null to run bare.</param>
    /// <param name="schemaName">The set's schema.</param>
    /// <param name="tableName">The event table name, <c>{history}_events</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="NotSupportedException">The provider records no events.</exception>
    Task EnsureEventsTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default) =>
        throw new NotSupportedException($"{GetType().Name} records no migration events.");

    /// <summary>Creates a set's seed journal if it does not exist.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, or null to run bare.</param>
    /// <param name="schemaName">The set's schema.</param>
    /// <param name="tableName">The seed journal name, such as <c>seed_history</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="NotSupportedException">The provider runs no seed files.</exception>
    Task EnsureSeedHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default) =>
        throw new NotSupportedException($"{GetType().Name} runs no seed files.");

    /// <summary>Returns the history-table reference, quoted and schema-qualified per the provider's identifier rules.</summary>
    /// <param name="schemaName">The schema that owns the history table; ignored by providers without schemas (e.g. SQLite).</param>
    /// <param name="tableName">The history table name.</param>
    string QualifyHistoryTable(string schemaName, string tableName);

    /// <summary>Quotes one identifier, such as a seed file's column name.</summary>
    /// <param name="identifier">The identifier, already checked to be snake_case.</param>
    string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

    /// <summary>Writes a seed value's parameter, cast to its column's type where the provider binds text strictly (a string into a <c>uuid</c> or <c>jsonb</c>).</summary>
    /// <param name="parameter">The parameter reference, such as <c>@p0</c>.</param>
    /// <param name="columnType">The column's type from <see cref="GetColumnTypesAsync"/>, or null when unknown.</param>
    string CastParameter(string parameter, string? columnType) => parameter;

    /// <summary>Creates the schema if it is missing; a provider without schemas does nothing.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, such as a dry run's, or null to run bare.</param>
    /// <param name="schemaName">The schema a set owns.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task EnsureSchemaAsync(DbConnection connection, DbTransaction? transaction, string schemaName, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>Returns whether a table exists, without creating anything — the read-only level check's probe.</summary>
    /// <remarks>The default body selects from the table and reads a provider error as absence, so call it outside a transaction.</remarks>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="transaction">The enclosing transaction, or null to read bare.</param>
    /// <param name="schemaName">The schema that would own the table; ignored by providers without schemas.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    async Task<bool> TableExistsAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default)
    {
        try
        {
            await connection.ExecuteScalarAsync(new CommandDefinition(
                $"SELECT 1 FROM {QualifyHistoryTable(schemaName, tableName)} WHERE 1 = 0", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }

    /// <summary>Returns a table's columns and their types, without creating anything; empty when the table is absent.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="transaction">The enclosing transaction, or null to read bare.</param>
    /// <param name="schemaName">The table's schema; ignored by providers without schemas.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Each column name with its type as the provider spells it, such as <c>integer</c> or <c>character varying(10)</c>.</returns>
    Task<IReadOnlyDictionary<string, string>> GetColumnTypesAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Lists every schema holding a table of the given name, such as each owned schema's owner-marker table.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="transaction">The enclosing transaction, or null to read bare.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<string>> FindTableSchemasAsync(DbConnection connection, DbTransaction? transaction, string tableName, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>Returns the installed extensions, or null for a provider without extensions.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlySet<string>?> GetInstalledExtensionsAsync(DbConnection connection, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>?>(null);

    /// <summary>Bounds how long each statement waits for a table lock: <c>SET LOCAL</c> inside a transaction, else for the session.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The batch transaction, or null for a no-transaction script.</param>
    /// <param name="timeout">The bound.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task SetLockTimeoutAsync(DbConnection connection, DbTransaction? transaction, TimeSpan timeout, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>Restores the session's lock-wait bound after a no-transaction script.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ResetLockTimeoutAsync(DbConnection connection, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Returns whether a failure means a lock was not granted in time, so the batch may retry.</summary>
    /// <param name="exception">The provider's exception.</param>
    bool IsLockNotAvailable(DbException exception) => false;

    /// <summary>Lists the other sessions that hold any of the given advisory-lock keys.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="keys">The set's keys.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<MigrationLockHolder>> GetLockHoldersAsync(DbConnection connection, IReadOnlyList<MigrationLockKey> keys, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MigrationLockHolder>>([]);

    /// <summary>Ends another session, releasing every lock it holds.</summary>
    /// <param name="connection">The open connection to signal from.</param>
    /// <param name="pid">The session's backend process id.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Whether the session was signalled.</returns>
    Task<bool> TerminateSessionAsync(DbConnection connection, int pid, CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>Replaces <see cref="MigrationConstants.SchemaPlaceholder"/> in a script with the set's schema, as this provider spells it.</summary>
    /// <remarks>The default writes the schema name; the runner checks the name is a safe identifier first.</remarks>
    /// <param name="sql">The migration script.</param>
    /// <param name="schemaName">The set's schema.</param>
    string SubstituteSchema(string sql, string schemaName) =>
        sql.Replace(MigrationConstants.SchemaPlaceholder, schemaName, StringComparison.Ordinal);

    /// <summary>Replaces one <c>{{schema:set}}</c> placeholder with the schema that set owns, as this provider spells it.</summary>
    /// <param name="sql">The migration script.</param>
    /// <param name="placeholder">The placeholder, such as <c>{{schema:identity}}</c>.</param>
    /// <param name="schemaName">The schema the named set owns.</param>
    string SubstituteSetSchema(string sql, string placeholder, string schemaName) =>
        sql.Replace(placeholder, schemaName, StringComparison.Ordinal);
}
