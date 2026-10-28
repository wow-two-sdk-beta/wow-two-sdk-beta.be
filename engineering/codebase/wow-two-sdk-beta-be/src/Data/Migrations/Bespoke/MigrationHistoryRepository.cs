using System.Data.Common;
using Dapper;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Accesses migration-history rows.</summary>
public sealed class MigrationHistoryRepository(IMigrationDialect dialect, MigrationOptions options) : IMigrationHistoryRepository
{
    private string QualifiedTable => dialect.QualifyHistoryTable(options.SchemaName, options.TableName);

    /// <inheritdoc />
    public Task EnsureTableAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct = default) =>
        dialect.EnsureHistoryTableAsync(connection, transaction, options.SchemaName, options.TableName, ct);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct = default) =>
        dialect.TableExistsAsync(connection, transaction, options.SchemaName, options.TableName, ct);

    /// <inheritdoc />
    /// <remarks>Reads the kind columns only when the table has them, so the read-only check works on a table no apply has upgraded.</remarks>
    public async Task<IReadOnlyList<MigrationHistoryEntry>> GetAppliedAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct = default)
    {
        var columns = await dialect.GetColumnTypesAsync(connection, transaction, options.SchemaName, options.TableName, ct);
        var kinds = columns.ContainsKey("kind") && columns.ContainsKey("contract_since")
            ? "kind, contract_since AS ContractSince"
            : "NULL AS Kind, NULL AS ContractSince";
        var sql = $"""
            SELECT ordinal,
                   version,
                   name,
                   checksum,
                   applied_at   AS AppliedAt,
                   applied_by   AS AppliedBy,
                   execution_ms AS ExecutionMs,
                   {kinds}
            FROM {QualifiedTable}
            ORDER BY ordinal
            """;
        var rows = await connection.QueryAsync<MigrationHistoryEntry>(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
        return rows.ToList();
    }

    /// <inheritdoc />
    public Task RecordAsync(DbConnection connection, DbTransaction? transaction, MigrationHistoryEntry entry, CancellationToken ct = default)
    {
        var sql = $"""
            INSERT INTO {QualifiedTable} (ordinal, version, name, checksum, applied_at, applied_by, execution_ms, kind, contract_since)
            VALUES (@Ordinal, @Version, @Name, @Checksum, @AppliedAt, @AppliedBy, @ExecutionMs, @Kind, @ContractSince)
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, entry, transaction, cancellationToken: ct));
    }

    /// <inheritdoc />
    public Task RemoveAsync(DbConnection connection, DbTransaction? transaction, int ordinal, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {QualifiedTable} WHERE ordinal = @ordinal", new { ordinal }, transaction, cancellationToken: ct));

    /// <inheritdoc />
    public Task UpdateChecksumAsync(DbConnection connection, DbTransaction? transaction, int ordinal, string checksum, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {QualifiedTable} SET checksum = @checksum WHERE ordinal = @ordinal",
            new { ordinal, checksum }, transaction, cancellationToken: ct));

    /// <inheritdoc />
    public Task UpdateKindAsync(DbConnection connection, DbTransaction? transaction, int ordinal, string kind, string? contractSince, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {QualifiedTable} SET kind = @kind, contract_since = @contractSince WHERE ordinal = @ordinal AND kind IS NULL",
            new { ordinal, kind, contractSince }, transaction, cancellationToken: ct));
}
