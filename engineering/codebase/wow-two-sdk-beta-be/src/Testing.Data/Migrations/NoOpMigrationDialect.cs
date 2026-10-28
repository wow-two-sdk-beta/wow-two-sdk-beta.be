using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>No-op <see cref="IMigrationDialect"/> keeping a startup migrate hook off the real database.</summary>
internal sealed class NoOpMigrationDialect : IMigrationDialect
{
    /// <inheritdoc />
    public MigrationCoordinationMode CoordinationMode => MigrationCoordinationMode.SingleApplicantRequired;

    /// <inheritdoc />
    public Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> TryAcquireLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task ReleaseLockAsync(DbConnection connection, MigrationLockKey key, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task EnsureHistoryTableAsync(DbConnection connection, DbTransaction? transaction, string schemaName, string tableName, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public string QualifyHistoryTable(string schemaName, string tableName) => $"\"{tableName}\"";
}
