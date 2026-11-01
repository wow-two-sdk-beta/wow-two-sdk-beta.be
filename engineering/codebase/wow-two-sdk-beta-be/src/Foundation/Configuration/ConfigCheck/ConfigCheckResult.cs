using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Represents one <c>config check</c> run: its findings, or the error that stopped it, and its exit.</summary>
/// <remarks>
///   - exits: <c>0</c> consistent · <c>1</c> refused (an invalid value, an unknown key or stranding) · <c>2</c> failed (<see cref="Error"/> set)
///   - carries setting names, codes and counts only; never an email, a phone number, a name, an id or a secret
///   - <see cref="ToResultLine"/> renders the <c>config-result {json}</c> line the command prints last
/// </remarks>
public sealed record ConfigCheckResult
{
    /// <summary>Holds the prefix of the result line: <c>config-result </c>.</summary>
    public const string ResultLinePrefix = "config-result ";

    /// <summary>Gets the verb: <c>check</c>.</summary>
    [JsonPropertyOrder(0)]
    public string Verb { get; init; } = ConfigCheckCommand.CheckVerb;

    /// <summary>Gets the process exit code: <c>2</c> with an error, <c>1</c> with any finding, else <c>0</c>.</summary>
    [JsonPropertyOrder(1)]
    public int Exit => Error is not null ? 2
        : (Invalid?.Count ?? 0) + (Unknown?.Count ?? 0) + (Stranded?.Count ?? 0) > 0 ? 1
        : 0;

    /// <summary>Gets the service name.</summary>
    [JsonPropertyOrder(2)]
    public required string Service { get; init; }

    /// <summary>Gets why the check could not finish, such as an unreachable database; set only on exit <c>2</c>, in place of the findings.</summary>
    [JsonPropertyOrder(3)]
    public string? Error { get; init; }

    /// <summary>Gets each missing or rejected value; null on exit <c>2</c>.</summary>
    [JsonPropertyOrder(4)]
    public IReadOnlyList<ConfigCheckInvalid>? Invalid { get; init; }

    /// <summary>Gets each key under the service's own sections that no settings record binds; null on exit <c>2</c>.</summary>
    [JsonPropertyOrder(5)]
    public IReadOnlyList<string>? Unknown { get; init; }

    /// <summary>Gets each stranding rule with a count above zero; null on exit <c>2</c>.</summary>
    [JsonPropertyOrder(6)]
    public IReadOnlyList<StrandedCount>? Stranded { get; init; }

    /// <summary>Gets the run's wall time in milliseconds.</summary>
    [JsonPropertyOrder(7)]
    public long DurationMs { get; init; }

    /// <summary>Renders the result as compact camelCase JSON; absent fields are left out.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ConfigCheckJsonContext.Default.ConfigCheckResult);

    /// <summary>Renders the <c>config-result {json}</c> line the command prints as its last stdout line.</summary>
    public string ToResultLine() => ResultLinePrefix + ToJson();

    /// <summary>Parses a run's JSON, as <see cref="ToJson"/> renders it.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The result, or null when the text is not a check result.</returns>
    public static ConfigCheckResult? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return JsonSerializer.Deserialize(json, ConfigCheckJsonContext.Default.ConfigCheckResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the last line starting <c>config-result </c> in a command's output and parses it.</summary>
    /// <param name="output">Everything the command wrote to standard output.</param>
    /// <returns>The result, or null when no line parses.</returns>
    public static ConfigCheckResult? FromOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var line = output.Split('\n').Select(text => text.TrimEnd('\r')).LastOrDefault(text => text.StartsWith(ResultLinePrefix, StringComparison.Ordinal));
        return line is null ? null : FromJson(line[ResultLinePrefix.Length..]);
    }
}

/// <summary>One missing or rejected setting value.</summary>
public sealed record ConfigCheckInvalid
{
    /// <summary>Gets the rule's short kebab-case id, such as <c>required</c> or <c>range</c>.</summary>
    [JsonPropertyOrder(0)]
    public required string Code { get; init; }

    /// <summary>Gets the full setting path, such as <c>Identity:Password:MinLength</c>.</summary>
    [JsonPropertyOrder(1)]
    public required string Setting { get; init; }

    /// <summary>Gets the rule broken; never a secret value.</summary>
    [JsonPropertyOrder(2)]
    public required string Message { get; init; }
}

/// <summary>Source-generated JSON for <see cref="ConfigCheckResult"/>: camelCase names, null fields left out.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigCheckResult))]
internal sealed partial class ConfigCheckJsonContext : JsonSerializerContext;
