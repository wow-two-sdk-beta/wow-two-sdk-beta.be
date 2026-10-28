using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents how one verb ended across its sets: the overall outcome, its exit code, and one entry per set.</summary>
/// <remarks>
///   - exits: <c>0</c> done or current · <c>1</c> refused · <c>2</c> failed · <c>3</c> busy; the most severe set decides
///   - <see cref="ToResultLine"/> renders the <c>migrate-result {json}</c> line a command prints last
/// </remarks>
public sealed record MigrateRunResult
{
    /// <summary>Gets the verb, one of <see cref="MigrateVerbs"/>.</summary>
    [JsonPropertyOrder(0)]
    public required string Verb { get; init; }

    /// <summary>Gets the process exit code the outcome maps to.</summary>
    [JsonPropertyOrder(1)]
    public int Exit => Outcome switch
    {
        MigrateOutcome.Refused => 1,
        MigrateOutcome.Failed => 2,
        MigrateOutcome.Busy => 3,
        _ => 0,
    };

    /// <summary>Gets the run's outcome: the most severe of its sets'.</summary>
    [JsonPropertyOrder(2)]
    public required MigrateOutcome Outcome { get; init; }

    /// <summary>Gets why the run was refused before any set ran, such as an unknown set name; null otherwise.</summary>
    [JsonPropertyOrder(3)]
    public string? Message { get; init; }

    /// <summary>Gets one entry per set, in run order.</summary>
    [JsonPropertyOrder(4)]
    public required IReadOnlyList<MigrateSetResult> Sets { get; init; }

    /// <summary>Combines set outcomes: failed, then refused, then busy, then done, then current.</summary>
    /// <param name="outcomes">The sets' outcomes.</param>
    public static MigrateOutcome Combine(IEnumerable<MigrateOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        return outcomes.Where(outcome => outcome != MigrateOutcome.Skipped).DefaultIfEmpty(MigrateOutcome.Current).Max();
    }

    /// <summary>Renders the result as compact JSON: camelCase names, enum values as lowercase words.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, MigrateResultJsonContext.Default.MigrateRunResult);

    /// <summary>Renders the <c>migrate-result {json}</c> line a migrate command prints as its last line.</summary>
    public string ToResultLine() => ResultLinePrefix + ToJson();

    /// <summary>Holds the prefix of the result line: <c>migrate-result </c>.</summary>
    public const string ResultLinePrefix = "migrate-result ";

    /// <summary>Parses a run's JSON, as <see cref="ToJson"/> renders it.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The result, or null when the text is not a run result.</returns>
    public static MigrateRunResult? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return JsonSerializer.Deserialize(json, MigrateResultJsonContext.Default.MigrateRunResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the last line starting <c>migrate-result </c> in a command's output and parses it.</summary>
    /// <param name="output">Everything the command wrote to standard output.</param>
    /// <returns>The result, or null when no line parses.</returns>
    public static MigrateRunResult? FromOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var line = output.Split('\n').Select(text => text.TrimEnd('\r')).LastOrDefault(text => text.StartsWith(ResultLinePrefix, StringComparison.Ordinal));
        return line is null ? null : FromJson(line[ResultLinePrefix.Length..]);
    }
}
