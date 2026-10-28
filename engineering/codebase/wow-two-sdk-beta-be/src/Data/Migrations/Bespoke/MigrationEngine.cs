using System.Data.Common;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Runs the migrate verbs over the registered sets and reports each run as a <see cref="MigrateRunResult"/>.</summary>
/// <remarks>
///   - reads every set's level before and after a writing verb, read-only, so each result carries levels, state and rollback floor
///   - maps a runner's failure carrying <see cref="MigrationConstants.BusyMetadataKey"/> to busy, any other to refused, and a thrown provider error to failed
///   - <c>apply</c> and <c>seed</c> stop at the first set that does not succeed; later sets report skipped
/// </remarks>
/// <param name="registry">The registered sets.</param>
public sealed class MigrationEngine(IMigrationSetRegistry registry) : IMigrationEngine
{
    /// <inheritdoc />
    public async Task<MigrateRunResult> CheckAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default)
    {
        var selection = Select(MigrateVerbs.Check, sets);
        if (selection.Refusal is { } refusal)
            return refusal;

        var names = MigrationSetOrder.Sort(registry, selection.Names);
        var unmet = await MigrationDependencyCheck.FindUnmetAsync(registry, names, runAppliesDependencies: false, ct);
        var results = new List<MigrateSetResult>(names.Count);
        foreach (var name in names)
        {
            var (report, failure) = await TryCheckAsync(name, ct);
            if (report is null)
            {
                results.Add(failure!);
                continue;
            }

            var dependencies = unmet.GetValueOrDefault(name) ?? [];
            var mayRun = report.MayRun && report.MissingExtensions.Count == 0 && dependencies.Count == 0;
            results.Add(FromReport(name, report) with
            {
                Status = mayRun ? MigrateOutcome.Current : MigrateOutcome.Refused,
                MissingDependencies = dependencies,
                Message = Explain(report, dependencies),
            });
        }

        return Run(MigrateVerbs.Check, results);
    }

    /// <inheritdoc />
    public async Task<MigrateRunResult> PlanAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default)
    {
        var selection = Select(MigrateVerbs.Plan, sets);
        if (selection.Refusal is { } refusal)
            return refusal;

        var results = new List<MigrateSetResult>();
        foreach (var name in MigrationSetOrder.Sort(registry, selection.Names))
        {
            try
            {
                if ((await registry.GetRunner(name).PlanAsync(ct)).IsFailure(out var error, out var plan))
                {
                    results.Add(Refused(name, error));
                    continue;
                }

                results.Add(Base(name) with
                {
                    Status = MigrateOutcome.Current,
                    LevelBefore = plan.AppliedLevel,
                    LevelAfter = plan.AppliedLevel,
                    Pending = plan.Pending.Select(MigrationLabel.Of).ToList(),
                    Message = plan.BelowLevel.Count == 0
                        ? null
                        : $"pending below the applied level {plan.AppliedLevel:D3}, refused outside a local run: {string.Join(", ", plan.BelowLevel.Select(m => m.Label))}",
                });
            }
            catch (Exception exception) when (IsRunFailure(exception))
            {
                results.Add(Failed(name, exception));
            }
        }

        return Run(MigrateVerbs.Plan, results);
    }

    /// <inheritdoc />
    public async Task<MigrateRunResult> ApplyAsync(
        IReadOnlyCollection<string>? sets = null, bool dryRun = false, string appliedBy = "deploy", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedBy);
        var selection = Select(MigrateVerbs.Apply, sets);
        if (selection.Refusal is { } refusal)
            return refusal;

        var names = MigrationSetOrder.Sort(registry, selection.Names);

        // Every refusal that needs no lock lands before any set runs SQL.
        var before = new Dictionary<string, MigrationLevelReport>(StringComparer.Ordinal);
        var preflight = new Dictionary<string, MigrateSetResult>(StringComparer.Ordinal);
        var unmet = await MigrationDependencyCheck.FindUnmetAsync(registry, names, runAppliesDependencies: true, ct);
        foreach (var name in names)
        {
            var (report, failure) = await TryCheckAsync(name, ct);
            if (report is null)
            {
                preflight[name] = failure!;
                continue;
            }

            before[name] = report;
            var dependencies = unmet.GetValueOrDefault(name) ?? [];
            if (dependencies.Count > 0 || report.MissingExtensions.Count > 0)
            {
                preflight[name] = FromReport(name, report) with
                {
                    Status = MigrateOutcome.Refused,
                    MissingDependencies = dependencies,
                    Message = Explain(report, dependencies),
                };
            }
        }

        if (preflight.Count > 0)
            return Run(MigrateVerbs.Apply, names.Select(name => preflight.GetValueOrDefault(name) ?? Skipped(name)).ToList());

        var results = new List<MigrateSetResult>(names.Count);
        foreach (var name in names)
        {
            var result = dryRun
                ? await RehearseAsync(name, before[name], ct)
                : await WriteAsync(name, before[name],
                    runner => runner.ApplyAsync(appliedBy, ct),
                    (result, report) => result with { Applied = report.Labels }, ct);
            results.Add(result);

            if (result.Status is not (MigrateOutcome.Done or MigrateOutcome.Current))
            {
                results.AddRange(names.Skip(results.Count).Select(Skipped));
                break;
            }
        }

        return Run(MigrateVerbs.Apply, results);
    }

    /// <inheritdoc />
    public async Task<MigrateRunResult> SeedAsync(IReadOnlyCollection<string>? sets = null, string appliedBy = "deploy", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedBy);
        var selection = Select(MigrateVerbs.Seed, sets);
        if (selection.Refusal is { } refusal)
            return refusal;

        var names = MigrationSetOrder.Sort(registry, selection.Names);
        var results = new List<MigrateSetResult>(names.Count);
        foreach (var name in names)
        {
            var result = await WriteAsync(name, before: null,
                runner => runner.SeedAsync(appliedBy, ct),
                (result, report) => result with { Seeds = report.Labels.Select(label => label.Label).ToList() }, ct);
            results.Add(result);

            if (result.Status is not (MigrateOutcome.Done or MigrateOutcome.Current))
            {
                results.AddRange(names.Skip(results.Count).Select(Skipped));
                break;
            }
        }

        return Run(MigrateVerbs.Seed, results);
    }

    /// <inheritdoc />
    public Task<MigrateRunResult> RollbackAsync(string? set, int toOrdinal, CancellationToken ct = default) =>
        OneSetAsync(MigrateVerbs.Rollback, set,
            runner => runner.RollbackToAsync(toOrdinal, ct),
            (result, report) => result with { Removed = report.Labels }, ct);

    /// <inheritdoc />
    public Task<MigrateRunResult> ForgetAsync(string? set, int aboveOrdinal, bool force = false, CancellationToken ct = default) =>
        OneSetAsync(MigrateVerbs.Forget, set,
            runner => runner.ForgetAboveAsync(aboveOrdinal, force, ct),
            (result, report) => result with { Removed = report.Labels }, ct);

    /// <inheritdoc />
    public Task<MigrateRunResult> RepairAsync(string? set, CancellationToken ct = default) =>
        OneSetAsync(MigrateVerbs.Repair, set,
            runner => runner.RepairChecksumsAsync(ct),
            (result, report) => result with { Repaired = report.Labels }, ct);

    /// <inheritdoc />
    public Task<MigrateRunResult> BaselineAsync(string? set, int? throughOrdinal = null, CancellationToken ct = default) =>
        OneSetAsync(MigrateVerbs.Baseline, set,
            runner => runner.BaselineToAsync(throughOrdinal, ct),
            (result, report) => result with { Applied = report.Labels }, ct);

    /// <inheritdoc />
    public Task<MigrateRunResult> ClaimAsync(string? set, string owner, string instance, CancellationToken ct = default) =>
        OneSetAsync(MigrateVerbs.Claim, set,
            runner => runner.ClaimSchemaAsync(owner, instance, ct),
            (result, report) => result with { Message = report.Detail ?? result.Message }, ct);

    /// <inheritdoc />
    public Task<MigrateSetsReport> DescribeAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default)
    {
        var selection = Select(MigrateVerbs.Sets, sets);
        if (selection.Refusal is { } refusal)
            return Task.FromResult(new MigrateSetsReport(refusal, []));

        var results = new List<MigrateSetResult>();
        var descriptions = new List<MigrateSetDescription>();
        foreach (var name in MigrationSetOrder.Sort(registry, selection.Names))
        {
            ct.ThrowIfCancellationRequested();
            if (MigrationSetDescriber.Describe(name, registry.GetOptions(name), registry.GetBroker(name)).IsFailure(out var error, out var description))
            {
                results.Add(Refused(name, error));
                continue;
            }

            descriptions.Add(description);
            results.Add(Base(name) with { Status = MigrateOutcome.Current, LevelAfter = description.Level });
        }

        return Task.FromResult(new MigrateSetsReport(Run(MigrateVerbs.Sets, results), descriptions));
    }

    /// <inheritdoc />
    public async Task<MigrateRunResult> LocksAsync(IReadOnlyCollection<string>? sets = null, CancellationToken ct = default)
    {
        var selection = Select(MigrateVerbs.Locks, sets);
        if (selection.Refusal is { } refusal)
            return refusal;

        var results = new List<MigrateSetResult>();
        foreach (var name in selection.Names)
        {
            try
            {
                var holders = (await registry.GetRunner(name).GetLockHoldersAsync(ct)).ValueOr([]);
                results.Add(Base(name) with { Status = MigrateOutcome.Current, Locks = holders });
            }
            catch (Exception exception) when (IsRunFailure(exception))
            {
                results.Add(Failed(name, exception));
            }
        }

        return Run(MigrateVerbs.Locks, results);
    }

    /// <inheritdoc />
    public Task<MigrateRunResult> UnlockAsync(string? set, CancellationToken ct = default) =>
        OneSetAsync(MigrateVerbs.Unlock, set,
            runner => runner.UnlockAsync(ct),
            (result, report) => result with { Message = report.Detail ?? result.Message }, ct);

    /// <summary>Runs a writing verb on one set, framed by read-only level checks.</summary>
    private async Task<MigrateRunResult> OneSetAsync(
        string verb, string? set, Func<IMigrationRunnerService, Task<Result<MigrationOperationReport>>> operation,
        Func<MigrateSetResult, MigrationOperationReport, MigrateSetResult> describe, CancellationToken ct)
    {
        if (set is null && registry.Names.Count != 1)
            return Refuse(verb, $"Name one set for {verb}; registered: {string.Join(", ", registry.Names)}.");

        var name = set ?? registry.Names[0];
        if (!registry.Contains(name))
            return Refuse(verb, $"No migration set named '{name}'; registered: {string.Join(", ", registry.Names)}.");

        var (before, failure) = await TryCheckAsync(name, ct);
        if (before is null && failure is { Status: MigrateOutcome.Failed })
            return Run(verb, [failure]);

        return Run(verb, [await WriteAsync(name, before, operation, describe, ct)]);
    }

    /// <summary>Runs one set's writing operation and reads its level after, mapping the outcome.</summary>
    private async Task<MigrateSetResult> WriteAsync(
        string name, MigrationLevelReport? before,
        Func<IMigrationRunnerService, Task<Result<MigrationOperationReport>>> operation,
        Func<MigrateSetResult, MigrationOperationReport, MigrateSetResult> describe, CancellationToken ct)
    {
        MigrateSetResult result;
        try
        {
            result = (await operation(registry.GetRunner(name))).IsFailure(out var error, out var report)
                ? Refused(name, error)
                : describe(Base(name) with
                {
                    Status = report.Changed ? MigrateOutcome.Done : MigrateOutcome.Current,
                    LockWaitMs = report.LockWaitMs,
                }, report);
        }
        catch (Exception exception) when (IsRunFailure(exception))
        {
            result = Failed(name, exception);
        }

        var (after, _) = await TryCheckAsync(name, ct);
        return result with
        {
            LevelBefore = before?.AppliedLevel,
            LevelAfter = after?.AppliedLevel,
            State = after?.State,
            RollbackFloor = after?.RollbackFloor,
            Message = result.Message ?? (after?.State == MigrationLevelState.AheadCompatible ? after.Describe() : null),
        };
    }

    /// <summary>Rehearses one set's pending migrations and reports what a real apply would run.</summary>
    private async Task<MigrateSetResult> RehearseAsync(string name, MigrationLevelReport before, CancellationToken ct)
    {
        try
        {
            if ((await registry.GetRunner(name).DryRunPendingAsync(ct)).IsFailure(out var error, out var rehearsal))
                return Refused(name, error) with { LevelBefore = before.AppliedLevel, LevelAfter = before.AppliedLevel, State = before.State };

            return FromReport(name, before) with
            {
                Status = rehearsal.Passed ? MigrateOutcome.Done : MigrateOutcome.Failed,
                Pending = rehearsal.WouldApply.Select(label => new MigrationLabel(label, null)).ToList(),
                Message = rehearsal.Failure is { } failed
                    ? $"rehearsal failed at {failed.Migration}: {failed.Error}"
                    : $"rehearsed {rehearsal.Succeeded.Count} of {rehearsal.WouldApply.Count} and rolled back; not rehearsed (no-transaction): {(rehearsal.NotRehearsed.Count == 0 ? "none" : string.Join(", ", rehearsal.NotRehearsed))}",
                SqlState = rehearsal.Failure?.SqlState,
            };
        }
        catch (Exception exception) when (IsRunFailure(exception))
        {
            return Failed(name, exception);
        }
    }

    /// <summary>Reads one set's level read-only, mapping a refusal or a provider error to a set result.</summary>
    private async Task<(MigrationLevelReport? Report, MigrateSetResult? Failure)> TryCheckAsync(string name, CancellationToken ct)
    {
        try
        {
            return (await registry.GetRunner(name).CheckLevelAsync(ct)).IsFailure(out var error, out var report)
                ? (null, Refused(name, error))
                : (report with { Set = name }, null);
        }
        catch (Exception exception) when (IsRunFailure(exception))
        {
            return (null, Failed(name, exception));
        }
    }

    private (IReadOnlyList<string> Names, MigrateRunResult? Refusal) Select(string verb, IReadOnlyCollection<string>? sets)
    {
        if (sets is null || sets.Count == 0)
            return (registry.Names, null);

        var unknown = sets.Where(name => !registry.Contains(name)).ToList();
        return unknown.Count > 0
            ? ([], Refuse(verb, $"No migration set named {string.Join(", ", unknown.Select(name => $"'{name}'"))}; registered: {string.Join(", ", registry.Names)}."))
            : (registry.Names.Where(sets.Contains).ToList(), null);
    }

    private MigrateSetResult Base(string name) => new()
    {
        Name = name,
        Schema = registry.GetOptions(name).SchemaName,
        Status = MigrateOutcome.Current,
        Release = registry.GetOptions(name).Release,
    };

    private MigrateSetResult FromReport(string name, MigrationLevelReport report) => Base(name) with
    {
        State = report.State,
        LevelBefore = report.AppliedLevel,
        LevelAfter = report.AppliedLevel,
        Pending = report.Pending.Select(label => new MigrationLabel(label, null)).ToList(),
        Orphaned = report.Orphaned,
        Drifted = report.Drifted,
        IncompatibleAhead = report.IncompatibleAhead,
        MissingExtensions = report.MissingExtensions,
        RollbackFloor = report.RollbackFloor,
    };

    private MigrateSetResult Refused(string name, AppError error) => Base(name) with
    {
        Status = error.Metadata?.TryGetValue(MigrationConstants.BusyMetadataKey, out var busy) == true && busy is true
            ? MigrateOutcome.Busy
            : MigrateOutcome.Refused,
        LockWaitMs = error.Metadata?.TryGetValue("lockWaitMs", out var waited) == true && waited is long waitMs ? waitMs : 0,
        Message = error.Message,
    };

    private MigrateSetResult Failed(string name, Exception exception) => Base(name) with
    {
        Status = MigrateOutcome.Failed,
        Message = exception.Message,
        SqlState = (exception as DbException)?.SqlState,
    };

    private MigrateSetResult Skipped(string name) => Base(name) with
    {
        Status = MigrateOutcome.Skipped,
        Message = "not attempted: an earlier set did not succeed",
    };

    private static string? Explain(MigrationLevelReport report, IReadOnlyList<string> dependencies)
    {
        var lines = new List<string>();
        if (report.State != MigrationLevelState.Current)
            lines.Add(report.Describe());
        if (report.MissingExtensions.Count > 0)
            lines.Add($"missing extensions: {string.Join(", ", report.MissingExtensions)}");
        lines.AddRange(dependencies);
        return lines.Count == 0 ? null : string.Join(" ", lines);
    }

    private static MigrateRunResult Run(string verb, IReadOnlyList<MigrateSetResult> sets) => new()
    {
        Verb = verb,
        Outcome = MigrateRunResult.Combine(sets.Select(set => set.Status)),
        Sets = sets,
    };

    private static MigrateRunResult Refuse(string verb, string message) => new()
    {
        Verb = verb,
        Outcome = MigrateOutcome.Refused,
        Message = message,
        Sets = [],
    };

    /// <summary>Tells a failed script, connection or configuration from a cancellation or a bug, which propagate.</summary>
    private static bool IsRunFailure(Exception exception) =>
        exception is DbException or InvalidOperationException or NotSupportedException or IOException or TimeoutException;
}
