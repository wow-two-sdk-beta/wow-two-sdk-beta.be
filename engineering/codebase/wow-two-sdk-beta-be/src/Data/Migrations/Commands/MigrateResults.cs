using System.Data.Common;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Builds the run results the tree reports itself: refusals before any set runs, skipped sets, a thrown failure, and the bare <c>migrate</c> run.</summary>
internal static class MigrateResults
{
    /// <summary>Builds a run refused before any set ran.</summary>
    /// <param name="verb">The verb.</param>
    /// <param name="message">Why it was refused.</param>
    public static MigrateRunResult Refuse(string verb, string message) => new()
    {
        Verb = verb,
        Outcome = MigrateOutcome.Refused,
        Message = message,
        Sets = [],
    };

    /// <summary>Builds the entry of a strategy set a halted run did not attempt.</summary>
    /// <param name="set">The strategy set.</param>
    public static MigrateSetResult Skipped(IMigrationStrategySet set) => new()
    {
        Name = set.Name,
        Schema = set.Schema,
        Status = MigrateOutcome.Skipped,
        Message = "not attempted: an earlier set did not succeed",
    };

    /// <summary>Classifies an exception that escaped a verb at the tree's boundary: configuration refuses (<c>1</c>), anything else fails (<c>2</c>).</summary>
    /// <param name="verb">The verb.</param>
    /// <param name="exception">The exception.</param>
    public static MigrateRunResult FromException(string verb, Exception exception) => new()
    {
        Verb = verb,
        Outcome = exception switch
        {
            DbException or IOException and not FileNotFoundException and not DirectoryNotFoundException or TimeoutException or OperationCanceledException => MigrateOutcome.Failed,
            OptionsValidationException or InvalidOperationException or ArgumentException or KeyNotFoundException
                or FileNotFoundException or DirectoryNotFoundException or NotSupportedException => MigrateOutcome.Refused,
            _ => MigrateOutcome.Failed,
        },
        Message = exception is OperationCanceledException ? "the run was canceled" : exception.Message,
        Sets = [],
    };

    /// <summary>Joins the bare <c>migrate</c> run: <c>apply</c>'s entries carrying each set's seeds, the worse status of the two winning.</summary>
    /// <param name="apply">The <c>apply</c> run.</param>
    /// <param name="seed">The <c>seed</c> run, or null when <c>apply</c> did not succeed.</param>
    public static MigrateRunResult Migrate(MigrateRunResult apply, MigrateRunResult? seed)
    {
        if (seed is null)
            return apply with { Verb = MigrateVerbs.Migrate };

        var seeded = seed.Sets.ToDictionary(set => set.Name, StringComparer.Ordinal);
        var sets = apply.Sets.Select(applied => seeded.TryGetValue(applied.Name, out var set)
            ? applied with
            {
                Status = MigrateRunResult.Combine([applied.Status, set.Status]),
                Seeds = set.Seeds,
                LockWaitMs = applied.LockWaitMs + set.LockWaitMs,
                Message = set.Message ?? applied.Message,
                SqlState = set.SqlState ?? applied.SqlState,
            }
            : applied).ToList();

        return new MigrateRunResult
        {
            Verb = MigrateVerbs.Migrate,
            Outcome = MigrateRunResult.Combine([apply.Outcome, seed.Outcome]),
            Message = seed.Message ?? apply.Message,
            Sets = sets,
        };
    }
}
