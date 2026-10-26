using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Maps between a process failure and the <see cref="AppError"/> that carries it, so a caller can word its own message per kind.</summary>
public static class ProcessFailureMapper
{
    /// <summary>Holds the metadata key carrying the <see cref="ProcessFailureKind"/>.</summary>
    public const string FailureKey = "processFailure";

    /// <summary>Holds the metadata key carrying the exit code of a process that exited.</summary>
    public const string ExitCodeKey = "exitCode";

    /// <summary>Reads the failure kind off an error a <see cref="IProcessClient"/> returned.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The kind, or <see langword="null"/> for an error that did not come from a process run.</returns>
    public static ProcessFailureKind? KindOf(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Metadata is not null
            && error.Metadata.TryGetValue(FailureKey, out object? value)
            && Enum.TryParse(value?.ToString(), out ProcessFailureKind kind)
                ? kind
                : null;
    }

    /// <summary>Marks <paramref name="error"/> with its failure kind and, for an exit, the exit code.</summary>
    /// <param name="error">The error to mark.</param>
    /// <param name="kind">Why the run failed.</param>
    /// <param name="exitCode">The exit code of a process that exited.</param>
    /// <returns>The error with its metadata extended.</returns>
    public static AppError Mark(AppError error, ProcessFailureKind kind, int? exitCode = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        var metadata = new Dictionary<string, object?>(error.Metadata ?? new Dictionary<string, object?>())
        {
            [FailureKey] = kind.ToString(),
        };
        if (exitCode is { } code)
        {
            metadata[ExitCodeKey] = code;
        }

        return error with { Metadata = metadata };
    }
}
