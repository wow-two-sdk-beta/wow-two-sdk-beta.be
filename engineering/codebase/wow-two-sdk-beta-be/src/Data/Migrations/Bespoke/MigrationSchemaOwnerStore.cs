using System.Data.Common;
using System.Globalization;
using Dapper;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Reads and records the marker row that names the set, owner and instance which claimed a set's schema.</summary>
/// <remarks>
///   - PostgreSQL keeps one marker table inside each owned schema; SQLite keeps one table keyed by the schema name
///   - the DDL is portable, and <c>claimed_at</c> is ISO 8601 text, so both dialects share it
/// </remarks>
/// <param name="dialect">The set's dialect, which qualifies the marker table and probes for it.</param>
/// <param name="options">The set's options: schema, set name, owner and instance.</param>
internal sealed class MigrationSchemaOwnerStore(IMigrationDialect dialect, MigrationOptions options)
{
    private string QualifiedTable => dialect.QualifyHistoryTable(options.SchemaName, MigrationConstants.SchemaOwnerTableName);

    /// <summary>Creates the marker table if it is missing.</summary>
    /// <param name="connection">The open connection to run the DDL on.</param>
    /// <param name="transaction">The enclosing transaction, or null to run bare.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task EnsureTableAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {QualifiedTable} (
                schema_name   varchar(63)  NOT NULL,
                set_name      varchar(63)  NOT NULL,
                owner         varchar(120) NOT NULL,
                instance      varchar(200) NOT NULL,
                claimed_at    varchar(40)  NOT NULL,
                claimed_by    varchar(60)  NOT NULL,
                CONSTRAINT pk_{MigrationConstants.SchemaOwnerTableName} PRIMARY KEY (schema_name)
            );
            """;
        return connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
    }

    /// <summary>Reads the schema's marker, or null when the table or the row is absent; never creates anything.</summary>
    /// <param name="connection">The open connection to query.</param>
    /// <param name="transaction">The enclosing transaction, or null to read bare.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<MigrationSchemaOwner?> ReadAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        if (!await dialect.TableExistsAsync(connection, transaction, options.SchemaName, MigrationConstants.SchemaOwnerTableName, ct))
            return null;

        var sql = $"""
            SELECT schema_name AS SchemaName,
                   set_name    AS SetName,
                   owner,
                   instance,
                   claimed_at  AS ClaimedAt,
                   claimed_by  AS ClaimedBy
            FROM {QualifiedTable}
            WHERE schema_name = @schemaName
            """;
        return await connection.QuerySingleOrDefaultAsync<MigrationSchemaOwner>(new CommandDefinition(
            sql, new { schemaName = options.SchemaName }, transaction, cancellationToken: ct));
    }

    /// <summary>Records this set's claim on its schema.</summary>
    /// <param name="connection">The open connection to write to.</param>
    /// <param name="transaction">The enclosing transaction, or null to write bare.</param>
    /// <param name="claimedAt">The claim time.</param>
    /// <param name="claimedBy">The host stamp, such as <c>cli</c> or <c>deploy</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task ClaimAsync(DbConnection connection, DbTransaction? transaction, DateTimeOffset claimedAt, string claimedBy, CancellationToken ct)
    {
        var sql = $"""
            INSERT INTO {QualifiedTable} (schema_name, set_name, owner, instance, claimed_at, claimed_by)
            VALUES (@SchemaName, @SetName, @Owner, @Instance, @ClaimedAt, @ClaimedBy)
            """;
        var row = new MigrationSchemaOwner
        {
            SchemaName = options.SchemaName,
            SetName = options.SetName,
            Owner = options.SchemaOwner ?? throw new InvalidOperationException("Only a set with a SchemaOwner claims its schema."),
            Instance = options.OwnerInstance ?? throw new InvalidOperationException("A set claims its schema only under an OwnerInstance."),
            ClaimedAt = claimedAt.ToString("O", CultureInfo.InvariantCulture),
            ClaimedBy = claimedBy,
        };
        return connection.ExecuteAsync(new CommandDefinition(sql, row, transaction, cancellationToken: ct));
    }

    /// <summary>Explains why a marker forbids this set its schema, or returns null when the marker names this set, owner and instance.</summary>
    /// <param name="marker">The recorded marker.</param>
    public string? DescribeConflict(MigrationSchemaOwner marker)
    {
        if (string.Equals(marker.SetName, options.SetName, StringComparison.Ordinal)
            && string.Equals(marker.Owner, options.SchemaOwner, StringComparison.Ordinal)
            && string.Equals(marker.Instance, options.OwnerInstance, StringComparison.Ordinal))
            return null;

        var claimant = options.SchemaOwner is { } owner
            ? $"this host runs set '{options.SetName}' as '{owner}' of instance '{options.OwnerInstance}'"
            : $"this host's set '{options.SetName}' declares no SchemaOwner";
        return $"Schema '{marker.SchemaName}' belongs to set '{marker.SetName}' as '{marker.Owner}' of instance '{marker.Instance}' " +
               $"(claimed {marker.ClaimedAt} by {marker.ClaimedBy}); {claimant}, so it may not migrate or use the schema.";
    }
}
