using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Lists every set a host registered — bespoke sets through the engine, EF and DbUp sets through their strategy — and runs a verb across them.</summary>
/// <remarks>
///   - bespoke sets run first, in dependency order, then each strategy set in registration order
///   - a run that stops at its first failure reports every later set skipped
/// </remarks>
internal sealed class MigrateSetCatalog
{
    private readonly IMigrationSetRegistry? _registry;
    private readonly IReadOnlyList<IMigrationStrategySet> _strategies;

    private MigrateSetCatalog(IMigrationEngine? engine, IMigrationSetRegistry? registry, IReadOnlyList<IMigrationStrategySet> strategies)
    {
        Engine = engine;
        _registry = registry;
        _strategies = strategies;
    }

    /// <summary>Gets the bespoke engine, or null when the host registered no bespoke set.</summary>
    public IMigrationEngine? Engine { get; }

    /// <summary>Gets the bespoke sets' registry, or null when the host registered none.</summary>
    public IMigrationSetRegistry? Registry => _registry;

    /// <summary>Gets every set name: bespoke sets in registration order, then strategy sets.</summary>
    public IReadOnlyList<string> Names => (_registry?.Names ?? []).Concat(_strategies.Select(set => set.Name)).ToList();

    /// <summary>Resolves the catalog from a host's services; resolving the registry validates the bespoke sets.</summary>
    /// <param name="services">The host's provider.</param>
    public static MigrateSetCatalog Create(IServiceProvider services)
    {
        var registry = services.GetService<IMigrationSetRegistry>();
        return new MigrateSetCatalog(
            registry is null ? null : services.GetRequiredService<IMigrationEngine>(),
            registry,
            services.GetServices<IMigrationStrategySet>().ToList());
    }

    /// <summary>Returns whether a set is a bespoke set.</summary>
    /// <param name="set">The set name.</param>
    public bool IsBespoke(string set) => _registry?.Contains(set) == true;

    /// <summary>Returns the strategy set by that name, or null.</summary>
    /// <param name="set">The set name.</param>
    public IMigrationStrategySet? Strategy(string set) => _strategies.FirstOrDefault(strategy => strategy.Name == set);

    /// <summary>Gets the strategy sets a run selects.</summary>
    /// <param name="set">The <c>--set</c> value, or null for every set.</param>
    public IReadOnlyList<IMigrationStrategySet> Strategies(string? set) =>
        _strategies.Where(strategy => set is null || strategy.Name == set).ToList();

    /// <summary>Returns whether a run selects any bespoke set.</summary>
    /// <param name="set">The <c>--set</c> value, or null for every set.</param>
    public bool SelectsBespoke(string? set) => Engine is not null && _registry!.Names.Count > 0 && (set is null || IsBespoke(set));

    /// <summary>Explains why a run cannot start: no set at all, two sets sharing a name, or an unknown <c>--set</c>.</summary>
    /// <param name="set">The <c>--set</c> value, or null for every set.</param>
    /// <returns>The refusal, or null when the run may start.</returns>
    public string? Refusal(string? set)
    {
        var names = Names;
        if (names.Count == 0)
            return "No migration set is registered; register one with AddDatabaseBespokeMigrations, AddDatabaseBespokeMigrationSet, AddEfMigrationsRunner or AddDbUpRunner.";

        var duplicate = names.GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            return $"Two migration sets are named '{duplicate.Key}'; give the EF or DbUp set its own SetName.";

        return set is null || names.Contains(set, StringComparer.Ordinal)
            ? null
            : $"No migration set named '{set}'; registered: {string.Join(", ", names)}.";
    }

    /// <summary>Resolves the one bespoke set a writing operator verb runs on.</summary>
    /// <param name="verb">The verb, for messages.</param>
    /// <param name="set">The <c>--set</c> value, or null to take the host's only set.</param>
    /// <returns>The set name, or the refusal.</returns>
    public (string? Set, string? Refusal) ResolveOne(string verb, string? set)
    {
        if (Refusal(set) is { } refusal)
            return (null, refusal);

        var names = Names;
        if (set is null && names.Count != 1)
            return (null, $"{verb} runs on one set; pass --set <name> (sets: {string.Join(", ", names)}).");

        var name = set ?? names[0];
        return Strategy(name) is { } strategy
            ? (null, $"The {strategy.Strategy} set '{name}' does not serve {verb}; its runner answers check, plan, apply, seed and sets.")
            : (name, null);
    }

    /// <summary>Runs a verb across the selected sets, bespoke sets through the engine first.</summary>
    /// <param name="verb">The verb.</param>
    /// <param name="set">The <c>--set</c> value, or null for every set.</param>
    /// <param name="stopOnFailure">Whether the first set that does not succeed skips the rest, as <c>apply</c> and <c>seed</c> do.</param>
    /// <param name="bespoke">Runs the verb on the selected bespoke sets: null names every one.</param>
    /// <param name="strategy">Runs the verb on one strategy set.</param>
    public async Task<MigrateRunResult> RunAsync(
        string verb, string? set, bool stopOnFailure,
        Func<IMigrationEngine, IReadOnlyCollection<string>?, Task<MigrateRunResult>> bespoke,
        Func<IMigrationStrategySet, Task<MigrateSetResult>> strategy)
    {
        if (Refusal(set) is { } refusal)
            return MigrateResults.Refuse(verb, refusal);

        var results = new List<MigrateSetResult>();
        var outcomes = new List<MigrateOutcome>();
        string? message = null;
        var halted = false;
        if (SelectsBespoke(set))
        {
            var run = await bespoke(Engine!, set is null ? null : [set]).ConfigureAwait(false);
            results.AddRange(run.Sets);
            outcomes.Add(run.Outcome);
            message = run.Message;
            halted = stopOnFailure && run.Exit != 0;
        }

        foreach (var selected in Strategies(set))
        {
            if (halted)
            {
                results.Add(MigrateResults.Skipped(selected));
                continue;
            }

            var result = await strategy(selected).ConfigureAwait(false);
            results.Add(result);
            outcomes.Add(result.Status);
            halted = stopOnFailure && result.Status is not (MigrateOutcome.Done or MigrateOutcome.Current);
        }

        return new MigrateRunResult
        {
            Verb = verb,
            Outcome = MigrateRunResult.Combine(outcomes),
            Message = message,
            Sets = results,
        };
    }
}
