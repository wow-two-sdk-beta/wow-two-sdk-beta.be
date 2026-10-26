using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors.Mappers;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Returns a failed tool call as an MCP tool error — <c>isError</c> with one text block holding a <see cref="McpToolError"/>
/// as JSON — instead of the transport's one generic message, so an agent can tell a retryable failure from a refused call.</summary>
/// <remarks>
/// A tool fails by throwing its <see cref="AppError"/> as an <see cref="AppException"/>, which keeps its own message. Any other
/// exception is logged and answered with the generic message of the kind <see cref="IExceptionMapper"/> maps it to, so exception
/// text — stack traces, paths, hosts, values — never reaches the agent. Cancellation by the caller and protocol errors, such as
/// a refused scope, pass through to the transport. <see cref="McpServiceCollectionExtensions.AddStatelessMcpServer"/> registers it.
/// </remarks>
public static partial class McpToolErrorFilter
{
    /// <summary>Wraps the tool-call pipeline.</summary>
    /// <param name="next">The next handler: the remaining filters and the tool.</param>
    /// <returns>The wrapped handler.</returns>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Apply(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not McpProtocolException
                && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                return Describe(request, exception);
            }
        };
    }

    /// <summary>Builds the tool result an agent reads for <paramref name="error"/>, for a tool that returns its failure instead of throwing it.</summary>
    /// <param name="error">The error the tool failed with.</param>
    /// <param name="natures">Decides which error kinds are transient; the SDK default when <see langword="null"/>.</param>
    /// <returns>An <c>isError</c> result holding the <see cref="McpToolError"/> as JSON.</returns>
    public static CallToolResult ToToolResult(AppError error, IErrorNatureMapper? natures = null) =>
        ToToolResult(McpToolError.From(error, natures ?? new ErrorNatureMapper()));

    private static CallToolResult Describe(RequestContext<CallToolRequestParams> request, Exception exception)
    {
        IServiceProvider? services = request.Services;
        IErrorNatureMapper natures = services?.GetService<IErrorNatureMapper>() ?? new ErrorNatureMapper();
        if (exception is AppException application)
        {
            return ToToolResult(McpToolError.From(application.Error, natures));
        }

        if (services?.GetService<ILoggerFactory>() is { } loggers)
        {
            ToolFailed(loggers.CreateLogger(typeof(McpToolErrorFilter)), exception, request.Params?.Name);
        }

        AppErrorType kind = (services?.GetService<IExceptionMapper>() ?? new ExceptionMapper([])).Map(exception).Type;
        return ToToolResult(McpToolError.Generic(kind, natures));
    }

    private static CallToolResult ToToolResult(McpToolError error) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(error, JsonOptionsConstants.Default) }],
    };

    [LoggerMessage(EventId = 9101, Level = LogLevel.Error, Message = "MCP tool {Tool} failed unexpectedly")]
    private static partial void ToolFailed(ILogger logger, Exception exception, string? tool);
}
