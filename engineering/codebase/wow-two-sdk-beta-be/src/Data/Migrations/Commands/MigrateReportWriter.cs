using System.Globalization;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Writes a verb's outcome: the human report on stderr, or the JSON report on stdout with <c>--json</c>, then the <c>migrate-result</c> line last on stdout.</summary>
/// <remarks>Redacts credentials from every message first, so neither stream carries a password or a connection string's secret.</remarks>
/// <param name="output">Standard output: the JSON report and the result line.</param>
/// <param name="error">Standard error: the human report.</param>
internal sealed partial class MigrateReportWriter(TextWriter output, TextWriter error)
{
    /// <summary>Writes one run's outcome.</summary>
    /// <param name="result">The run's result.</param>
    /// <param name="json">Whether to write the JSON report on stdout instead of the human report on stderr.</param>
    /// <param name="sets">The <c>sets</c> descriptions, or null for any other verb.</param>
    /// <returns>The written result, redacted.</returns>
    public MigrateRunResult Write(MigrateRunResult result, bool json, IReadOnlyList<MigrateSetDescription>? sets = null)
    {
        result = Redact(result);
        if (json)
            output.WriteLine(sets is null ? result.ToJson() : new MigrateSetsReport(result, sets).ToJson());
        else
            WriteHuman(result, sets);

        output.WriteLine(result.ToResultLine());
        output.Flush();
        error.Flush();
        return result;
    }

    /// <summary>Writes parse errors to stderr, then the refused result line.</summary>
    /// <param name="verb">The verb the arguments named, or the command's name.</param>
    /// <param name="errors">The parse errors.</param>
    public void WriteParseErrors(string verb, IEnumerable<string> errors)
    {
        var messages = errors.ToList();
        foreach (var message in messages)
            error.WriteLine($"✗ {message}");
        output.WriteLine(Redact(MigrateResults.Refuse(verb, string.Join(" ", messages))).ToResultLine());
        output.Flush();
        error.Flush();
    }

    /// <summary>Redacts credentials from a text: <c>Password=…</c> pairs and the password of a <c>postgres://user:password@host</c> URI.</summary>
    /// <param name="text">The text, or null.</param>
    public static string? Redact(string? text) =>
        text is null ? null : UriCredential().Replace(Credential().Replace(text, "$1=***"), "$1***@");

    private static MigrateRunResult Redact(MigrateRunResult result) => result with
    {
        Message = Redact(result.Message),
        Sets = result.Sets.Select(set => set with { Message = Redact(set.Message) }).ToList(),
    };

    private void WriteHuman(MigrateRunResult result, IReadOnlyList<MigrateSetDescription>? sets)
    {
        error.WriteLine($"{Mark(result.Outcome)} {result.Verb}: {Word(result.Outcome)} (exit {result.Exit.ToString(CultureInfo.InvariantCulture)})");
        if (result.Message is { } message)
            error.WriteLine($"  {message}");

        foreach (var set in result.Sets)
        {
            var state = set.State is { } level ? $", {Word(level)}" : "";
            var levels = set.LevelBefore is { } before && set.LevelAfter is { } after && before != after
                ? $", level {before:D3} → {after:D3}"
                : set.LevelAfter is { } current ? $", level {current:D3}" : "";
            error.WriteLine($"  {Mark(set.Status)} {set.Name} [{set.Schema}]: {Word(set.Status)}{state}{levels}");
            Line("applied", set.Applied.Select(Describe));
            Line("rolled back or forgotten", set.Removed.Select(Describe));
            Line("repaired", set.Repaired.Select(Describe));
            Line("pending", set.Pending.Select(Describe));
            Line("seeded", set.Seeds);
            Line("orphaned", set.Orphaned.Select(ordinal => ordinal.ToString("D3", CultureInfo.InvariantCulture)));
            Line("drifted", set.Drifted);
            Line("incompatible ahead", set.IncompatibleAhead);
            Line("missing extensions", set.MissingExtensions);
            Line("missing dependencies", set.MissingDependencies);
            Line("locks", set.Locks.Select(holder => $"pid {holder.Pid} ({holder.ApplicationName ?? "no application_name"}, {holder.Key})"));
            if (set.RollbackFloor is { } floor)
                error.WriteLine($"      rollback floor: {floor}");
            if (set.Message is { } detail)
                error.WriteLine($"      {detail}");
        }

        foreach (var description in sets ?? [])
        {
            var contracts = description.Contracts.Count == 0 ? "no contracts" : string.Join(", ", description.Contracts.Select(c => $"{c.Label} since {c.Since}"));
            var package = description.Package is null ? "filesystem" : $"{description.Package} {description.Version}".TrimEnd();
            error.WriteLine($"  · {description.Name} ({description.Strategy}, {package}): level {description.Level:D3}, {contracts}");
        }
    }

    private void Line(string label, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count > 0)
            error.WriteLine($"      {label}: {string.Join(", ", list)}");
    }

    private static string Describe(MigrationLabel label) => label.Kind is { } kind ? $"{label.Label} ({Word(kind)})" : label.Label;

    private static string Mark(MigrateOutcome outcome) => outcome switch
    {
        MigrateOutcome.Current or MigrateOutcome.Done => "✓",
        MigrateOutcome.Skipped => "·",
        MigrateOutcome.Busy => "…",
        _ => "✗",
    };

    private static string Word<TEnum>(TEnum value) where TEnum : struct, Enum =>
        WordBoundary().Replace(value.ToString(), " ").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    [GeneratedRegex(@"(?i)\b(password|pwd)\s*=\s*[^;\s""']*")]
    private static partial Regex Credential();

    [GeneratedRegex(@"(?i)(\b[a-z][a-z0-9+.-]*://[^:/@\s]+:)[^@\s]+@")]
    private static partial Regex UriCredential();
}
