using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes one bespoke set from its options and its source alone: no connection, no settings.</summary>
internal static class MigrationSetDescriber
{
    /// <summary>The strategy name a bespoke set reports.</summary>
    public const string Strategy = "bespoke";

    /// <summary>Reads the set's source and headers into a description.</summary>
    /// <param name="name">The set name.</param>
    /// <param name="options">The set's effective options.</param>
    /// <param name="broker">The set's source.</param>
    /// <returns>The description, or a <c>Validation</c> failure on a malformed folder name or header.</returns>
    public static Result<MigrateSetDescription> Describe(string name, MigrationOptions options, IMigrationBroker broker)
    {
        if (broker.Read().IsFailure(out var sourceError, out var raw))
            return Result<MigrateSetDescription>.Fail(sourceError);

        var level = 0;
        var contracts = new List<MigrateSetContract>();
        var requires = new List<MigrateSetRequirement>();
        foreach (var migration in raw.OrderBy(migration => migration.Name, StringComparer.Ordinal))
        {
            var match = MigrationConstants.FolderPattern().Match(migration.Name);
            if (!match.Success)
            {
                return Result<MigrateSetDescription>.Fail(AppErrorFactory.Validation(
                    $"Migration folder '{migration.Name}' must match NNN-name (e.g. 001-baseline)."));
            }

            if (MigrationHeaderParser.Parse(migration.Name, migration.ApplySql).IsFailure(out var headerError, out var header))
                return Result<MigrateSetDescription>.Fail(headerError);

            var ordinal = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var label = $"{ordinal:D3}-{match.Groups[2].Value}";
            level = Math.Max(level, ordinal);
            if (header.Kind == MigrationKind.Contract)
                contracts.Add(new MigrateSetContract(label, header.ContractSince!));
            requires.AddRange(header.Requires.Select(requirement => new MigrateSetRequirement(label, requirement)));
        }

        var assembly = (broker as EmbeddedResourceMigrationBroker)?.Assembly;
        return Result<MigrateSetDescription>.Ok(new MigrateSetDescription
        {
            Name = name,
            Schema = options.SchemaName,
            Strategy = Strategy,
            OwnerKind = options.SchemaOwner,
            Level = level,
            Contracts = contracts,
            Requires = requires,
            DependsOn = options.Dependencies.ToList(),
            Extensions = options.RequiredExtensions.ToList(),
            ReferenceableTables = options.ReferenceableTables.ToList(),
            Package = assembly?.GetName().Name,
            Version = assembly is null ? null : MigrationRelease.TextOf(assembly),
            Release = options.Release,
        });
    }
}
