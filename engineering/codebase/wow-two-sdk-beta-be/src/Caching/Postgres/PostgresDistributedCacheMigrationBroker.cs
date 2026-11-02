using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Caching.Postgres;

/// <summary>Reads the cache's embedded migration set under a renamed table: every bare <c>distributed_cache</c> identifier becomes the configured name.</summary>
/// <remarks>
///   - used only for a non-default <see cref="PostgresDistributedCacheOptions.TableName"/>; the default name reads the resources as embedded
///   - the checksum covers the rewritten script, so the name is fixed once the set is applied
///   - names that merely contain it, such as <c>ix_distributed_cache_expires_at</c>, stay as written
/// </remarks>
/// <param name="embedded">The set as embedded in the SDK assembly.</param>
/// <param name="tableName">The configured table, a lowercase identifier.</param>
internal sealed partial class PostgresDistributedCacheMigrationBroker(EmbeddedResourceMigrationBroker embedded, string tableName) : IMigrationBroker
{
    /// <inheritdoc />
    public Result<IReadOnlyList<RawMigration>> Read()
    {
        // The host's options are not validated on this path, so the name is checked before it reaches any script.
        if (!MigrationConstants.IdentifierPattern().IsMatch(tableName))
        {
            return Result<IReadOnlyList<RawMigration>>.Fail(AppErrorFactory.Validation(
                $"Caching:Postgres:TableName '{tableName}' must be a lowercase identifier (a-z, 0-9 and _, 63 characters at most)."));
        }

        if (embedded.Read().IsFailure(out var error, out var migrations))
            return Result<IReadOnlyList<RawMigration>>.Fail(error);

        return Result<IReadOnlyList<RawMigration>>.Ok(migrations
            .Select(migration => migration with
            {
                ApplySql = Rename(migration.ApplySql),
                RollbackSql = Rename(migration.RollbackSql),
            })
            .ToList());
    }

    /// <inheritdoc />
    public Result<IReadOnlyList<RawSeed>> ReadSeeds() => embedded.ReadSeeds();

    private string Rename(string sql) => DefaultTable().Replace(sql, tableName);

    /// <summary>Matches the default table name as a whole identifier.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_])distributed_cache(?![A-Za-z0-9_])")]
    private static partial Regex DefaultTable();
}
