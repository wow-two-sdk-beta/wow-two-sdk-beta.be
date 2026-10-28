using System.Data.Common;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Fills <c>{{schema:set}}</c> placeholders with the schema each named set owns, read from the database's owner markers.</summary>
/// <remarks>Runs right before the scripts execute, after the checksum is taken, so a dependency's schema never changes a checksum.</remarks>
internal static class MigrationSetSchemaResolver
{
    /// <summary>Resolves every placeholder in the given migrations' Apply and Rollback scripts.</summary>
    /// <param name="dialect">The set's dialect, which spells the schema.</param>
    /// <param name="owners">The set's owner-marker store, which finds another set's schema.</param>
    /// <param name="connection">The open connection to read the markers on.</param>
    /// <param name="transaction">The enclosing transaction, or null to read bare.</param>
    /// <param name="migrations">The migrations about to run.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The migrations with schemas filled, or a <c>Conflict</c> failure naming a set that owns no schema in this database.</returns>
    public static async Task<Result<List<MigrationDescriptor>>> ResolveAsync(
        IMigrationDialect dialect, MigrationSchemaOwnerStore owners, DbConnection connection, DbTransaction? transaction,
        IReadOnlyList<MigrationDescriptor> migrations, CancellationToken ct)
    {
        var schemas = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolved = new List<MigrationDescriptor>(migrations.Count);

        foreach (var migration in migrations)
        {
            var apply = migration.ApplySql;
            var rollback = migration.RollbackSql;
            var names = MigrationConstants.SetSchemaPlaceholderPattern().Matches(apply + "\n" + rollback)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal);

            foreach (var name in names)
            {
                if (!schemas.TryGetValue(name, out var schema))
                {
                    var owned = await owners.FindSchemaOfSetAsync(connection, transaction, name, ct).ConfigureAwait(false);
                    if (owned is null || !MigrationConstants.SchemaNamePattern().IsMatch(owned))
                    {
                        return Result<List<MigrationDescriptor>>.Fail(AppErrorFactory.Conflict(
                            $"Migration '{migration.Label}' uses {{{{schema:{name}}}}}, but no schema in this database is claimed by set '{name}'; " +
                            "apply that set first, or claim its schema."));
                    }

                    schema = owned;
                    schemas[name] = schema;
                }

                var placeholder = $"{{{{schema:{name}}}}}";
                apply = dialect.SubstituteSetSchema(apply, placeholder, schema);
                rollback = dialect.SubstituteSetSchema(rollback, placeholder, schema);
            }

            resolved.Add(ReferenceEquals(apply, migration.ApplySql) && ReferenceEquals(rollback, migration.RollbackSql)
                ? migration
                : migration with { ApplySql = apply, RollbackSql = rollback });
        }

        return Result<List<MigrationDescriptor>>.Ok(resolved);
    }
}
