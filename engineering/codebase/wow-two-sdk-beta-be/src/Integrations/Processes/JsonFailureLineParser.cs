using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Parses a failed process's last standard-error line as one JSON object carrying a display-safe reason, such as
/// <c>{"reason":"target is locked","failure":"Rejected"}</c>.</summary>
/// <remarks>
/// Only the reason reaches the error, stripped of control characters and capped at <see cref="MaxReasonLength"/>; the process
/// owns keeping it display-safe — static messages and key names, never values or command output. Any other line, an empty
/// reason or malformed JSON yields <see langword="null"/>, so the client returns its generic error.
/// </remarks>
public sealed class JsonFailureLineParser : IProcessFailureParser
{
    /// <summary>Gets the property holding the reason. Defaults to <c>reason</c>.</summary>
    public string ReasonProperty { get; init; } = "reason";

    /// <summary>Gets the property holding the failure kind the <see cref="Kinds"/> map reads. Defaults to <c>failure</c>.</summary>
    public string KindProperty { get; init; } = "failure";

    /// <summary>Gets the error type per failure kind, such as <c>Rejected</c> → <see cref="AppErrorType.Conflict"/>. Empty by default.</summary>
    public IDictionary<string, AppErrorType> Kinds { get; init; } = new Dictionary<string, AppErrorType>(StringComparer.Ordinal);

    /// <summary>Gets the error type of a failure whose kind is absent or unmapped. Defaults to <see cref="AppErrorType.ExternalUnavailable"/>.</summary>
    public AppErrorType DefaultType { get; init; } = AppErrorType.ExternalUnavailable;

    /// <summary>Gets the longest reason kept, in characters. Defaults to 300.</summary>
    public int MaxReasonLength { get; init; } = 300;

    /// <summary>Gets the message built from the error type and the reason, such as a caller's prefix; the reason alone when <see langword="null"/>.</summary>
    public Func<AppErrorType, string, string>? Format { get; init; }

    /// <inheritdoc />
    public AppError? Parse(int exitCode, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);
        string? line = standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (line is null)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(ReasonProperty, out JsonElement reason)
                || reason.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string text = new string([.. reason.GetString()!.Where(character => !char.IsControl(character)).Take(MaxReasonLength)]).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            AppErrorType type = root.TryGetProperty(KindProperty, out JsonElement kind)
                && kind.ValueKind == JsonValueKind.String
                && Kinds.TryGetValue(kind.GetString()!, out AppErrorType mapped)
                    ? mapped
                    : DefaultType;
            return AppError.Of(type, Format?.Invoke(type, text) ?? text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
