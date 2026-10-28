using System.Reflection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Integrates migration scripts and seed files stored as assembly resources.</summary>
/// <remarks>
///   - use at runtime so the schema ships inside the binary — no filesystem dependency at deploy
///   - seeds default to the folder beside the migrations: <c>Migrations/</c> reads <c>Seeds/</c>, <c>Migrations/Postgres/</c> reads <c>Seeds/Postgres/</c>,
///     and a prefix without a <c>Migrations</c> segment reads its own <c>Seeds/</c> subfolder
/// </remarks>
/// <param name="assembly">The assembly that embeds the resources.</param>
/// <param name="folderPrefix">The resource prefix of the <c>NNN-name/</c> migration folders.</param>
/// <param name="seedPrefix">The resource prefix of the <c>NNN-name.json</c> seed files, or null for the folder beside the migrations.</param>
public sealed class EmbeddedResourceMigrationBroker(Assembly assembly, string folderPrefix = "Migrations/", string? seedPrefix = null) : IMigrationBroker
{
    private readonly string _folderPrefix = folderPrefix;
    private readonly string _seedPrefix = seedPrefix ?? DefaultSeedPrefix(folderPrefix);

    /// <summary>Gets the assembly that embeds the set, whose version is the set owner's release.</summary>
    public Assembly Assembly { get; } = assembly;

    /// <inheritdoc />
    public Result<IReadOnlyList<RawMigration>> Read()
    {
        var apply = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rollback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in Assembly.GetManifestResourceNames())
        {
            var normalized = resourceName.Replace('\\', '/');
            if (!normalized.StartsWith(_folderPrefix, StringComparison.OrdinalIgnoreCase) ||
                !normalized.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = normalized[_folderPrefix.Length..]; // e.g. "001-baseline/Apply.sql"
            var slash = relative.IndexOf('/');
            if (slash <= 0)
                continue;

            var folder = relative[..slash];
            var file = relative[(slash + 1)..];

            // Bucket each resource by folder into apply / rollback.
            if (file.Equals(MigrationConstants.ApplyFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (ReadResource(resourceName).IsFailure(out var applyError, out var applyResourceSql))
                    return Result<IReadOnlyList<RawMigration>>.Fail(applyError);

                apply[folder] = applyResourceSql;
            }
            else if (file.Equals(MigrationConstants.RollbackFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (ReadResource(resourceName).IsFailure(out var rollbackError, out var rollbackResourceSql))
                    return Result<IReadOnlyList<RawMigration>>.Fail(rollbackError);

                rollback[folder] = rollbackResourceSql;
            }
        }

        var migrations = new List<RawMigration>(apply.Count);
        foreach (var (folder, applySql) in apply)
        {
            if (!rollback.TryGetValue(folder, out var rollbackSql))
            {
                return Result<IReadOnlyList<RawMigration>>.Fail(AppErrorFactory.FileNotFound(
                    $"Migration '{folder}' is missing {MigrationConstants.RollbackFileName}."));
            }

            migrations.Add(new RawMigration
            {
                Name = folder,
                ApplySql = applySql,
                RollbackSql = rollbackSql,
            });
        }

        return Result<IReadOnlyList<RawMigration>>.Ok(migrations);
    }

    /// <inheritdoc />
    public Result<IReadOnlyList<RawSeed>> ReadSeeds()
    {
        var seeds = new List<RawSeed>();
        foreach (var resourceName in Assembly.GetManifestResourceNames())
        {
            var normalized = resourceName.Replace('\\', '/');
            if (!normalized.StartsWith(_seedPrefix, StringComparison.OrdinalIgnoreCase)
                || !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var file = normalized[_seedPrefix.Length..];
            if (file.Contains('/', StringComparison.Ordinal))
                continue;

            if (ReadResource(resourceName).IsFailure(out var error, out var json))
                return Result<IReadOnlyList<RawSeed>>.Fail(error);

            seeds.Add(new RawSeed { Name = file[..^".json".Length], Json = json });
        }

        return Result<IReadOnlyList<RawSeed>>.Ok(seeds);
    }

    /// <summary>Derives the seed prefix beside a migrations prefix: its <c>Migrations</c> segment becomes <c>Seeds</c>.</summary>
    /// <param name="folderPrefix">The migrations prefix.</param>
    private static string DefaultSeedPrefix(string folderPrefix)
    {
        var segments = folderPrefix.Replace('\\', '/').TrimEnd('/').Split('/');
        var index = Array.FindIndex(segments, segment => segment.Equals("Migrations", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return folderPrefix.TrimEnd('/') + "/" + MigrationConstants.SeedsFolderName + "/";

        segments[index] = MigrationConstants.SeedsFolderName;
        return string.Join('/', segments) + "/";
    }

    private Result<string> ReadResource(string name)
    {
        try
        {
            using var stream = Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return Result<string>.Fail(AppErrorFactory.DataIntegrity(
                    $"Embedded migration resource '{name}' could not be opened."));
            }

            using var reader = new StreamReader(stream);
            return Result<string>.Ok(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail(AppErrorFactory.DataIntegrity(
                $"Embedded migration resource '{name}' could not be read.", exception));
        }
    }
}
