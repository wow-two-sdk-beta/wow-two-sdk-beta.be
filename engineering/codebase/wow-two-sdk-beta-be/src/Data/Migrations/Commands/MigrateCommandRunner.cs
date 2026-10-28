using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Provides the body of every verb: opens the host's services, guards, runs the engine or the strategy sets, and writes the outcome.</summary>
/// <remarks>
///   - every verb ends in exactly one <c>migrate-result</c> line on stdout and returns its exit: <c>0</c> done or current · <c>1</c> refused · <c>2</c> failed · <c>3</c> busy
///   - a writing operator verb runs on one bespoke set and needs <c>--confirm &lt;database&gt;</c> matching the set's database; it carries no prompt and no force
///   - an exception that escapes a verb is classified at this boundary: configuration refuses, anything else fails
/// </remarks>
/// <param name="host">Where the tree runs: an image's services or the tool's provider.</param>
/// <param name="writer">Writes each outcome.</param>
internal sealed class MigrateCommandRunner(IMigrateCommandHost host, MigrateReportWriter writer)
{
    /// <summary>Holds why <c>seed</c> changes nothing on a strategy set.</summary>
    private const string NoSeedFiles = "its runner carries no seed files";

    /// <summary>Runs <c>check</c>: <c>0</c> when the image may run on every set, else <c>1</c>; writes nothing.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> CheckAsync(MigrateInvocation invocation, CancellationToken ct) =>
        RunAsync(invocation, (_, sets) => sets.RunAsync(invocation.Verb, invocation.Set, stopOnFailure: false,
            (engine, names) => engine.CheckAsync(names, ct), strategy => strategy.CheckAsync(ct)), ct);

    /// <summary>Runs <c>plan</c>: every pending migration with its kind; takes no lock and writes nothing.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> PlanAsync(MigrateInvocation invocation, CancellationToken ct) =>
        RunAsync(invocation, (_, sets) => sets.RunAsync(invocation.Verb, invocation.Set, stopOnFailure: false,
            (engine, names) => engine.PlanAsync(names, ct), strategy => strategy.PlanAsync(ct)), ct);

    /// <summary>Runs <c>apply</c>: creates each database when missing, then applies every set in order, or rehearses with <c>--dry-run</c>.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="dryRun">Whether to rehearse and roll back.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> ApplyAsync(MigrateInvocation invocation, bool dryRun, CancellationToken ct) =>
        RunAsync(invocation, (scope, sets) => ApplyAsync(scope, sets, invocation.Verb, invocation.Set, dryRun, ct), ct);

    /// <summary>Runs <c>seed</c>: every set's seed files not yet journalled, in order.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> SeedAsync(MigrateInvocation invocation, CancellationToken ct) =>
        RunAsync(invocation, (scope, sets) => SeedAsync(scope, sets, invocation.Verb, invocation.Set, ct), ct);

    /// <summary>Runs the bare <c>migrate</c> a rollout declares: <c>apply</c>, then <c>seed</c> once every set applied.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> MigrateAsync(MigrateInvocation invocation, CancellationToken ct) =>
        RunAsync(invocation, async (scope, sets) =>
        {
            var apply = await ApplyAsync(scope, sets, MigrateVerbs.Apply, invocation.Set, dryRun: false, ct).ConfigureAwait(false);
            var seed = apply.Exit == 0 ? await SeedAsync(scope, sets, MigrateVerbs.Seed, invocation.Set, ct).ConfigureAwait(false) : null;
            return MigrateResults.Migrate(apply, seed);
        }, ct);

    /// <summary>Runs <c>sets</c>: describes every set from the image alone; opens no database.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> SetsAsync(MigrateInvocation invocation, CancellationToken ct)
    {
        var descriptions = new List<MigrateSetDescription>();
        return await RunAsync(invocation, async (_, sets) =>
        {
            var run = await sets.RunAsync(invocation.Verb, invocation.Set, stopOnFailure: false,
                async (engine, names) =>
                {
                    var report = await engine.DescribeAsync(names, ct).ConfigureAwait(false);
                    descriptions.AddRange(report.Sets);
                    return report.Result;
                },
                async strategy =>
                {
                    var description = await strategy.DescribeAsync(ct).ConfigureAwait(false);
                    descriptions.Add(description);
                    return new MigrateSetResult { Name = strategy.Name, Schema = strategy.Schema, Status = MigrateOutcome.Current, LevelAfter = description.Level };
                }).ConfigureAwait(false);
            return run;
        }, ct, () => descriptions).ConfigureAwait(false);
    }

    /// <summary>Runs <c>rollback --to N</c> on one set, confirmed.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="toOrdinal">The level to return to.</param>
    /// <param name="confirm">The <c>--confirm</c> value.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> RollbackAsync(MigrateInvocation invocation, int toOrdinal, string? confirm, CancellationToken ct) =>
        RunGuardedAsync(invocation, confirm, (engine, set) => engine.RollbackAsync(set, toOrdinal, ct), ct);

    /// <summary>Runs <c>forget --above N</c> on one set, confirmed; a contract or kind-less row needs <c>--include-contracts</c>.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="aboveOrdinal">The level to keep.</param>
    /// <param name="includeContracts">Whether to forget a contract or a kind-less row too.</param>
    /// <param name="confirm">The <c>--confirm</c> value.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> ForgetAsync(MigrateInvocation invocation, int aboveOrdinal, bool includeContracts, string? confirm, CancellationToken ct) =>
        RunGuardedAsync(invocation, confirm, (engine, set) => engine.ForgetAsync(set, aboveOrdinal, includeContracts, ct), ct);

    /// <summary>Runs <c>repair</c> on one set, confirmed.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="confirm">The <c>--confirm</c> value.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> RepairAsync(MigrateInvocation invocation, string? confirm, CancellationToken ct) =>
        RunGuardedAsync(invocation, confirm, (engine, set) => engine.RepairAsync(set, ct), ct);

    /// <summary>Runs <c>baseline --to N</c> on one set, confirmed.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="throughOrdinal">The last ordinal to record, or null for every source migration.</param>
    /// <param name="confirm">The <c>--confirm</c> value.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> BaselineAsync(MigrateInvocation invocation, int? throughOrdinal, string? confirm, CancellationToken ct) =>
        RunGuardedAsync(invocation, confirm, (engine, set) => engine.BaselineAsync(set, throughOrdinal, ct), ct);

    /// <summary>Runs <c>claim --owner o --instance i</c> on one set, confirmed.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="owner">The owner kind.</param>
    /// <param name="instance">The instance.</param>
    /// <param name="confirm">The <c>--confirm</c> value.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> ClaimAsync(MigrateInvocation invocation, string owner, string instance, string? confirm, CancellationToken ct) =>
        RunGuardedAsync(invocation, confirm, (engine, set) => engine.ClaimAsync(set, owner, instance, ct), ct);

    /// <summary>Runs <c>locks</c>: the sessions holding each set's migrator locks, read-only.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> LocksAsync(MigrateInvocation invocation, CancellationToken ct) =>
        RunAsync(invocation, (_, sets) => sets.RunAsync(invocation.Verb, invocation.Set, stopOnFailure: false,
            (engine, names) => engine.LocksAsync(names, ct), strategy => Task.FromResult(Unserved(strategy, invocation.Verb))), ct);

    /// <summary>Runs <c>unlock</c> on one set, confirmed: ends every session holding its migrator locks.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    /// <param name="confirm">The <c>--confirm</c> value.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task<int> UnlockAsync(MigrateInvocation invocation, string? confirm, CancellationToken ct) =>
        RunGuardedAsync(invocation, confirm, (engine, set) => engine.UnlockAsync(set, ct), ct);

    /// <summary>Answers <c>bg</c>: refused until background migrations ship.</summary>
    /// <param name="invocation">The parsed invocation.</param>
    public int Background(MigrateInvocation invocation)
    {
        writer.Write(MigrateResults.Refuse(invocation.Verb, "Background migrations ship later; the bg verbs have nothing to control yet."), invocation.Json);
        return 1;
    }

    private async Task<MigrateRunResult> ApplyAsync(
        IMigrateCommandScope scope, MigrateSetCatalog sets, string verb, string? set, bool dryRun, CancellationToken ct)
    {
        // A fresh box may have no database yet; a rehearsal never creates one.
        if (!dryRun && sets.Refusal(set) is null && sets.SelectsBespoke(set))
            await scope.EnsureDatabasesAsync(ct).ConfigureAwait(false);

        return await sets.RunAsync(verb, set, stopOnFailure: true,
            (engine, names) => engine.ApplyAsync(names, dryRun, scope.AppliedBy, ct),
            strategy => strategy.ApplyAsync(dryRun, ct)).ConfigureAwait(false);
    }

    private static Task<MigrateRunResult> SeedAsync(IMigrateCommandScope scope, MigrateSetCatalog sets, string verb, string? set, CancellationToken ct) =>
        sets.RunAsync(verb, set, stopOnFailure: true,
            (engine, names) => engine.SeedAsync(names, scope.AppliedBy, ct),
            strategy => Task.FromResult(new MigrateSetResult
            {
                Name = strategy.Name,
                Schema = strategy.Schema,
                Status = MigrateOutcome.Current,
                Message = $"the {strategy.Strategy} set: {NoSeedFiles}",
            }));

    private Task<int> RunGuardedAsync(
        MigrateInvocation invocation, string? confirm, Func<IMigrationEngine, string, Task<MigrateRunResult>> operation, CancellationToken ct) =>
        RunAsync(invocation, (_, sets) =>
        {
            var (set, refusal) = sets.ResolveOne(invocation.Verb, invocation.Set);
            if (refusal is not null)
                return Task.FromResult(MigrateResults.Refuse(invocation.Verb, refusal));

            return Confirm(sets.Registry!, set!, invocation.Verb, confirm) is { } unconfirmed
                ? Task.FromResult(MigrateResults.Refuse(invocation.Verb, unconfirmed))
                : operation(sets.Engine!, set!);
        }, ct);

    /// <summary>Compares <c>--confirm</c> ordinally with the database the set connects to, read without connecting.</summary>
    /// <returns>The refusal with a rerun hint, or null when confirmed.</returns>
    private static string? Confirm(IMigrationSetRegistry registry, string set, string verb, string? confirm)
    {
        var database = registry.GetRunner(set).ReadDatabaseName();
        if (database is null)
            return $"{verb} cannot read which database set '{set}' connects to, so it cannot be confirmed.";
        if (string.IsNullOrEmpty(confirm))
            return $"{verb} writes outside a rollout; rerun with --confirm {database} to confirm the database.";

        return string.Equals(confirm, database, StringComparison.Ordinal)
            ? null
            : $"--confirm '{confirm}' does not name the database set '{set}' connects to; rerun with --confirm {database}.";
    }

    private static MigrateSetResult Unserved(IMigrationStrategySet strategy, string verb) => new()
    {
        Name = strategy.Name,
        Schema = strategy.Schema,
        Status = MigrateOutcome.Refused,
        Message = $"The {strategy.Strategy} set '{strategy.Name}' does not serve {verb}; its runner answers check, plan, apply, seed and sets.",
    };

    /// <summary>Opens the host's services, runs one verb body, classifies an escaping exception, and writes the outcome.</summary>
    private async Task<int> RunAsync(
        MigrateInvocation invocation, Func<IMigrateCommandScope, MigrateSetCatalog, Task<MigrateRunResult>> body, CancellationToken ct,
        Func<IReadOnlyList<MigrateSetDescription>>? descriptions = null)
    {
        MigrateRunResult result;
        try
        {
            if ((await host.OpenAsync(invocation, ct).ConfigureAwait(false)).IsFailure(out var refused, out var scope))
            {
                result = MigrateResults.Refuse(invocation.Verb, refused.Message);
            }
            else
            {
                await using (scope.ConfigureAwait(false))
                    result = await body(scope, MigrateSetCatalog.Create(scope.Services)).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            result = MigrateResults.FromException(invocation.Verb, exception);
        }

        return writer.Write(result, invocation.Json, descriptions?.Invoke()).Exit;
    }
}
