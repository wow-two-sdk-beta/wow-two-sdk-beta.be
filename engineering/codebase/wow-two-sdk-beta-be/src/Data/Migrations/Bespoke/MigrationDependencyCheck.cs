using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Finds each set's unmet dependencies before any SQL runs: a referenced set missing from the database or below the level it names.</summary>
/// <remarks>
///   - a referenced set registered in this image and part of the run counts at its source level, since the run applies it first
///   - any other referenced set counts at its applied level, read from its history; a set this image lacks is found through the owner markers
/// </remarks>
internal static class MigrationDependencyCheck
{
    /// <summary>Lists the unmet dependencies of every set in the run.</summary>
    /// <param name="registry">The registered sets.</param>
    /// <param name="runSets">The sets the run covers.</param>
    /// <param name="runAppliesDependencies">Whether the run applies its sets, so a referenced set in the run counts at its source level.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Each set with unmet dependencies, mapped to one line per dependency.</returns>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindUnmetAsync(
        IMigrationSetRegistry registry, IReadOnlyCollection<string> runSets, bool runAppliesDependencies, CancellationToken ct)
    {
        var unmet = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var set in runSets)
        {
            var problems = new List<string>();
            foreach (var dependency in registry.GetOptions(set).Dependencies)
            {
                if (await DescribeAsync(registry, set, dependency, runSets, runAppliesDependencies, ct).ConfigureAwait(false) is { } problem)
                    problems.Add(problem);
            }

            if (problems.Count > 0)
                unmet[set] = problems;
        }

        return unmet;
    }

    private static async Task<string?> DescribeAsync(
        IMigrationSetRegistry registry, string set, MigrationDependency dependency, IReadOnlyCollection<string> runSets,
        bool runAppliesDependencies, CancellationToken ct)
    {
        var needs = $"needs set '{dependency.Set}' at {Level(dependency.Level)}";
        if (!registry.Contains(dependency.Set))
        {
            return await registry.GetRunner(set).ReadSetLevelAsync(dependency.Set, ct).ConfigureAwait(false) switch
            {
                null => $"{needs}, which this database lacks",
                var level when level < dependency.Level => $"{needs}, found {Level(level.Value)}",
                _ => null,
            };
        }

        if ((await registry.GetRunner(dependency.Set).CheckLevelAsync(ct).ConfigureAwait(false)).IsFailure(out var error, out var report))
            return $"{needs}, which could not be checked: {error.Message}";

        if (runAppliesDependencies && runSets.Contains(dependency.Set))
            return report.ExpectedLevel < dependency.Level ? $"{needs}, but this image ships it only up to {Level(report.ExpectedLevel)}" : null;

        return report.AppliedLevel switch
        {
            0 => $"{needs}, which this database lacks",
            var level when level < dependency.Level => $"{needs}, found {Level(level)}",
            _ => null,
        };
    }

    private static string Level(int ordinal) => ordinal.ToString("D3", CultureInfo.InvariantCulture);
}
