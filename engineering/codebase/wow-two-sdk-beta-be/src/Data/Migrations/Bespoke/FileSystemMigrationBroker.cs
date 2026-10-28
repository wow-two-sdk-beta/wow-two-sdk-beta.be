using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Integrates migration scripts and seed files stored on the local filesystem.</summary>
/// <param name="migrationsRoot">The folder containing the <c>NNN-name</c> directories.</param>
/// <param name="seedsRoot">The folder containing the <c>NNN-name.json</c> seed files, or null for <c>Seeds</c> beside the migrations root.</param>
public sealed class FileSystemMigrationBroker(string migrationsRoot, string? seedsRoot = null) : IMigrationBroker
{
    /// <summary>Gets the migrations root — the folder containing the <c>NNN-name</c> directories.</summary>
    public string Root { get; } = migrationsRoot;

    /// <summary>Gets the seeds root — the folder containing the <c>NNN-name.json</c> seed files.</summary>
    public string SeedsRoot { get; } = seedsRoot
        ?? Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(migrationsRoot))) ?? ".", MigrationConstants.SeedsFolderName);

    /// <inheritdoc />
    public Result<IReadOnlyList<RawMigration>> Read()
    {
        try
        {
            if (!Directory.Exists(Root))
                return Result<IReadOnlyList<RawMigration>>.Ok([]);

            var migrations = new List<RawMigration>();
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var applyPath = Path.Combine(dir, MigrationConstants.ApplyFileName);
                if (!File.Exists(applyPath))
                    continue; // Skip the Dev folder and anything without an Apply script.

                var rollbackPath = Path.Combine(dir, MigrationConstants.RollbackFileName);
                if (!File.Exists(rollbackPath))
                {
                    return Result<IReadOnlyList<RawMigration>>.Fail(AppErrorFactory.FileNotFound(
                        $"Migration '{Path.GetFileName(dir)}' is missing {MigrationConstants.RollbackFileName}."));
                }

                migrations.Add(new RawMigration
                {
                    Name = Path.GetFileName(dir),
                    ApplySql = File.ReadAllText(applyPath),
                    RollbackSql = File.ReadAllText(rollbackPath),
                });
            }

            return Result<IReadOnlyList<RawMigration>>.Ok(migrations);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<RawMigration>>.Fail(AppErrorFactory.DataIntegrity(
                $"Migration source '{Root}' could not be read.", exception));
        }
    }

    /// <inheritdoc />
    public Result<IReadOnlyList<RawSeed>> ReadSeeds()
    {
        try
        {
            if (!Directory.Exists(SeedsRoot))
                return Result<IReadOnlyList<RawSeed>>.Ok([]);

            var seeds = Directory.GetFiles(SeedsRoot, "*.json")
                .Select(path => new RawSeed { Name = Path.GetFileNameWithoutExtension(path), Json = File.ReadAllText(path) })
                .ToList();
            return Result<IReadOnlyList<RawSeed>>.Ok(seeds);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<RawSeed>>.Fail(AppErrorFactory.DataIntegrity(
                $"Seed source '{SeedsRoot}' could not be read.", exception));
        }
    }
}
