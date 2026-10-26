using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors.Mappers;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;

namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Represents a failed tool call as an agent reads it: the error kind, the display-safe message and whether the same
/// call may succeed later.</summary>
public sealed record McpToolError
{
    /// <summary>Gets the error kind, named as REST problem documents name it, such as <c>OperationTimeout</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the display-safe message: the application's own text, the one REST returns as <c>detail</c>; never exception
    /// text, paths, hosts or values.</summary>
    public required string Message { get; init; }

    /// <summary>Gets whether retrying the same call may succeed: a timeout, throttling or an unavailable dependency, not a refused,
    /// invalid, missing or forbidden request.</summary>
    public required bool Retryable { get; init; }

    /// <summary>Describes an application error with its own message.</summary>
    /// <param name="error">The error a tool failed with.</param>
    /// <param name="natures">Decides which error kinds are transient.</param>
    /// <returns>The error the agent reads.</returns>
    public static McpToolError From(AppError error, IErrorNatureMapper natures)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(natures);
        return new McpToolError
        {
            Code = error.Type.ToString(),
            Message = error is ValidationError { Failures.Count: > 0 } validation
                ? string.Join(" ", validation.Failures.Select(failure => failure.Message))
                : error.Message,
            Retryable = natures.Map(error.Type) == ErrorNature.Transient,
        };
    }

    /// <summary>Describes an error kind with its generic message, for a failure whose own text is not display-safe.</summary>
    /// <param name="type">The kind the failure maps to.</param>
    /// <param name="natures">Decides which error kinds are transient.</param>
    /// <returns>The error the agent reads.</returns>
    public static McpToolError Generic(AppErrorType type, IErrorNatureMapper natures)
    {
        ArgumentNullException.ThrowIfNull(natures);
        return new McpToolError
        {
            Code = type.ToString(),
            Message = ErrorMessageConstants.For(type),
            Retryable = natures.Map(type) == ErrorNature.Transient,
        };
    }
}
