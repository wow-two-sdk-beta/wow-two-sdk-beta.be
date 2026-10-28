using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents the migrations one set applied in a multi-set run.</summary>
/// <param name="Set">The set name.</param>
/// <param name="Applied">The labels the set applied, empty when it was up to date.</param>
public sealed record MigrationSetApplied(string Set, IReadOnlyList<string> Applied);

/// <summary>Provides the multi-set entry points a host calls: the deploy step that applies, and the startup check that only reads.</summary>
/// <remarks>
///   - production applies migrations in an explicit deploy step (<c>wow-migrate apply</c>, or the image's own migrate command calling <see cref="ApplyMigrationSetsAsync"/>)
///   - a production host boots with <see cref="VerifyMigrationLevelsOnStartupAsync"/>; applying at boot is a development option
///   - neither creates the database: the CLI or the host's database bootstrap does
/// </remarks>
public static partial class MigrationSetServiceProviderExtensions
{
    private const string LoggerCategory = "WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.MigrationSets";

    /// <summary>Applies every registered set's pending migrations in dependency order, or only the named sets, stopping at the first failure.</summary>
    /// <remarks>
    ///   - every set runs after the registered sets it depends on; registration order breaks ties
    ///   - before any SQL, refuses with <c>Conflict</c> when a set's dependency is missing from the database or below the level it names
    /// </remarks>
    /// <param name="services">The root service provider.</param>
    /// <param name="appliedBy">The host stamp recorded on each applied row, such as <c>deploy</c>, <c>cli</c> or <c>startup</c>.</param>
    /// <param name="setNames">The sets to run, or null for every set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Each run set's applied labels, or the first set's failure prefixed with its name, or a <c>NotFound</c> failure for an unknown set.</returns>
    /// <exception cref="System.Data.Common.DbException">A migration script failed; later sets are not attempted.</exception>
    public static async Task<Result<IReadOnlyList<MigrationSetApplied>>> ApplyMigrationSetsAsync(
        this IServiceProvider services, string appliedBy, IReadOnlyCollection<string>? setNames = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedBy);

        var registry = services.GetRequiredService<IMigrationSetRegistry>();
        if (Select(registry, setNames).IsFailure(out var unknown, out var selected))
            return Result<IReadOnlyList<MigrationSetApplied>>.Fail(unknown);

        var names = MigrationSetOrder.Sort(registry, selected);
        var unmet = await MigrationDependencyCheck.FindUnmetAsync(registry, names, runAppliesDependencies: true, ct).ConfigureAwait(false);
        if (unmet.Count > 0)
        {
            return Result<IReadOnlyList<MigrationSetApplied>>.Fail(AppErrorFactory.Conflict(string.Join(" ", unmet.Select(pair =>
                $"Set '{pair.Key}' {string.Join("; ", pair.Value)}."))));
        }

        var applied = new List<MigrationSetApplied>(names.Count);
        foreach (var name in names)
        {
            if ((await registry.GetRunner(name).ApplyPendingAsync(appliedBy, ct).ConfigureAwait(false)).IsFailure(out var error, out var labels))
                return Result<IReadOnlyList<MigrationSetApplied>>.Fail(error with { Message = $"Set '{name}': {error.Message}" });

            applied.Add(new MigrationSetApplied(name, labels));
        }

        return Result<IReadOnlyList<MigrationSetApplied>>.Ok(applied);
    }

    /// <summary>Checks every registered set's level, or only the named sets, without writing anything.</summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="setNames">The sets to check, or null for every set.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>One report per checked set, or the first set's failure prefixed with its name, or a <c>NotFound</c> failure for an unknown set.</returns>
    public static async Task<Result<IReadOnlyList<MigrationLevelReport>>> CheckMigrationLevelsAsync(
        this IServiceProvider services, IReadOnlyCollection<string>? setNames = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetRequiredService<IMigrationSetRegistry>();
        if (Select(registry, setNames).IsFailure(out var unknown, out var names))
            return Result<IReadOnlyList<MigrationLevelReport>>.Fail(unknown);

        var reports = new List<MigrationLevelReport>(names.Count);
        foreach (var name in names)
        {
            if ((await registry.GetRunner(name).CheckLevelAsync(ct).ConfigureAwait(false)).IsFailure(out var error, out var report))
                return Result<IReadOnlyList<MigrationLevelReport>>.Fail(error with { Message = $"Set '{name}': {error.Message}" });

            // The default body of CheckLevelAsync cannot know the set it runs for.
            reports.Add(report with { Set = name });
        }

        return Result<IReadOnlyList<MigrationLevelReport>>.Ok(reports);
    }

    /// <summary>Stops the host when a set's database level differs from the code's — the production boot step, run in place of applying.</summary>
    /// <remarks>
    ///   - reads only: never migrates, creates or claims anything
    ///   - a compatible ahead (newer history this release survives) boots with a warning, under every policy
    ///   - a set under <see cref="MigrationLevelPolicy.Warn"/> logs any other difference and boots; an owner conflict always stops the host
    /// </remarks>
    /// <param name="services">The root service provider.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="AppException">A set's check failed, such as on a malformed source.</exception>
    /// <exception cref="InvalidOperationException">A set's schema belongs to another owner, or its database is behind, incompatibly ahead, orphaned or drifted under <see cref="MigrationLevelPolicy.Fail"/>.</exception>
    public static async Task VerifyMigrationLevelsOnStartupAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The host has no result channel before its pipeline exists, so a failed check throws.
        var reports = (await services.CheckMigrationLevelsAsync(ct: ct).ConfigureAwait(false)).ValueOrThrow();
        var registry = services.GetRequiredService<IMigrationSetRegistry>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;

        var failures = new List<string>();
        foreach (var report in reports)
        {
            if (report.IsCurrent)
            {
                LogLevelCurrent(logger, report.Set, report.AppliedLevel);
                continue;
            }

            // A rollback onto history this release survives must boot: the newer release's expands are still in place.
            if (report.State == MigrationLevelState.AheadCompatible)
            {
                LogCompatibleAhead(logger, report.Describe());
                continue;
            }

            if (report.State == MigrationLevelState.OwnerConflict
                || registry.GetOptions(report.Set).LevelMismatch == MigrationLevelPolicy.Fail)
            {
                failures.Add(report.Describe());
                continue;
            }

            LogLevelMismatch(logger, report.Describe());
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The database does not match this build; run the migrate deploy step for this release before starting it. " +
                string.Join(" ", failures));
        }
    }

    /// <summary>Selects the sets to run in registration order, failing on a name no set has.</summary>
    /// <param name="registry">The registered sets.</param>
    /// <param name="setNames">The requested sets, or null or empty for every set.</param>
    private static Result<IReadOnlyList<string>> Select(IMigrationSetRegistry registry, IReadOnlyCollection<string>? setNames)
    {
        if (setNames is null || setNames.Count == 0)
            return Result<IReadOnlyList<string>>.Ok(registry.Names);

        var unknown = setNames.Where(name => !registry.Contains(name)).ToList();
        if (unknown.Count > 0)
        {
            return Result<IReadOnlyList<string>>.Fail(AppErrorFactory.NotFound(
                $"No migration set named {string.Join(", ", unknown.Select(name => $"'{name}'"))}; registered: {string.Join(", ", registry.Names)}."));
        }

        return Result<IReadOnlyList<string>>.Ok(registry.Names.Where(setNames.Contains).ToList());
    }

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "Migration set {Set} is current at {Level}.")]
    private static partial void LogLevelCurrent(ILogger logger, string set, int level);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning, Message = "Migration level mismatch, starting anyway: {Report}")]
    private static partial void LogLevelMismatch(ILogger logger, string report);

    [LoggerMessage(EventId = 3024, Level = LogLevel.Warning, Message = "Migration set ahead of this release by migrations it survives, starting: {Report}")]
    private static partial void LogCompatibleAhead(ILogger logger, string report);
}
