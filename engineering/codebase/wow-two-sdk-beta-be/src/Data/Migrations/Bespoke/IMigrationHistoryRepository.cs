using System.Data.Common;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines access to the migration-history table.</summary>
public interface IMigrationHistoryRepository
{
    /// <summary>Creates the history table if missing and adds the columns an older table lacks — safe to call on every apply.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, such as a dry run's, or null to run bare.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task EnsureTableAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct = default);

    /// <summary>Returns whether the history table exists, without creating it.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="transaction">The enclosing transaction, or null to read bare.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<bool> ExistsAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct = default);

    /// <summary>Reads all applied migrations, ordered by ordinal; a table from an older SDK reads with no kinds.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="transaction">The enclosing transaction, such as a dry run's, or null to read bare.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<MigrationHistoryEntry>> GetAppliedAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct = default);

    /// <summary>Inserts an applied-migration row.</summary>
    /// <param name="connection">The open connection to write to.</param>
    /// <param name="transaction">The enclosing transaction, or null when the migration runs bare.</param>
    /// <param name="entry">The history row to insert.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task RecordAsync(DbConnection connection, DbTransaction? transaction, MigrationHistoryEntry entry, CancellationToken ct = default);

    /// <summary>Deletes a migration row by ordinal — used by rollback and forget.</summary>
    /// <param name="connection">The open connection to write to.</param>
    /// <param name="transaction">The enclosing transaction, or null when run bare.</param>
    /// <param name="ordinal">The ordinal of the row to delete.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task RemoveAsync(DbConnection connection, DbTransaction? transaction, int ordinal, CancellationToken ct = default);

    /// <summary>Re-records the stored checksum of a migration — used by repair.</summary>
    /// <param name="connection">The open connection to write to.</param>
    /// <param name="transaction">The enclosing transaction, or null when run bare.</param>
    /// <param name="ordinal">The ordinal of the row to update.</param>
    /// <param name="checksum">The new checksum to store.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task UpdateChecksumAsync(DbConnection connection, DbTransaction? transaction, int ordinal, string checksum, CancellationToken ct = default);

    /// <summary>Records the kind of a row applied before kinds existed, read from the source file the image carries.</summary>
    /// <param name="connection">The open connection to write to.</param>
    /// <param name="transaction">The enclosing transaction, or null when run bare.</param>
    /// <param name="ordinal">The ordinal of the row to update.</param>
    /// <param name="kind">The kind as stored: <c>expand</c> or <c>contract</c>.</param>
    /// <param name="contractSince">A contract's <c>since</c>, or null.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task UpdateKindAsync(DbConnection connection, DbTransaction? transaction, int ordinal, string kind, string? contractSince, CancellationToken ct = default);
}
