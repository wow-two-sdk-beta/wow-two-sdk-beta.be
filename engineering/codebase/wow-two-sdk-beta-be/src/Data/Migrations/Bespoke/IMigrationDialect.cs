using System.Data.Common;
using Dapper;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines provider-specific migration SQL: database and schema creation, locking, history DDL and schema substitution.</summary>
public interface IMigrationDialect
{
    /// <summary>Gets the provider's simultaneous-applicant guarantee.</summary>
    MigrationCoordinationMode CoordinationMode { get; }

    /// <summary>Creates the target database if it is missing, returning whether a create occurred.</summary>
    /// <param name="connectionString">The connection string whose database name is ensured.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default);

    /// <summary>Acquires the apply-loop advisory lock, blocking until it is granted.</summary>
    /// <param name="connection">The open connection that holds the lock for its session.</param>
    /// <param name="lockId">The advisory-lock id shared by every host.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task AcquireLockAsync(DbConnection connection, long lockId, CancellationToken ct = default);

    /// <summary>Releases the apply-loop advisory lock held by the session.</summary>
    /// <param name="connection">The connection that holds the lock.</param>
    /// <param name="lockId">The advisory-lock id to release.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ReleaseLockAsync(DbConnection connection, long lockId, CancellationToken ct = default);

    /// <summary>Creates the migration-history table for the given schema and name if it does not exist.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, such as a dry run's, or null to run bare.</param>
    /// <param name="schemaName">The schema that owns the history table.</param>
    /// <param name="tableName">The history table name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task EnsureHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default);

    /// <summary>Returns the history-table reference, quoted and schema-qualified per the provider's identifier rules.</summary>
    /// <param name="schemaName">The schema that owns the history table; ignored by providers without schemas (e.g. SQLite).</param>
    /// <param name="tableName">The history table name.</param>
    string QualifyHistoryTable(string schemaName, string tableName);

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

    /// <summary>Replaces <see cref="MigrationConstants.SchemaPlaceholder"/> in a script with the set's schema, as this provider spells it.</summary>
    /// <remarks>The default writes the schema name; the runner checks the name is a safe identifier first.</remarks>
    /// <param name="sql">The migration script.</param>
    /// <param name="schemaName">The set's schema.</param>
    string SubstituteSchema(string sql, string schemaName) =>
        sql.Replace(MigrationConstants.SchemaPlaceholder, schemaName, StringComparison.Ordinal);
}
