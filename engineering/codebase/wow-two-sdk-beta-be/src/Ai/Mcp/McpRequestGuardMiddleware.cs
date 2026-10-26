using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Screens a request to an MCP endpoint before the transport reads it: a foreign browser origin gets 403 and a body
/// over the limit gets 413.</summary>
/// <remarks>
/// <see cref="McpEndpointRouteBuilderExtensions.MapAuthenticatedMcp"/> wraps every endpoint it maps with this screen, so it runs
/// after authentication and authorization and before the transport. A request without an <c>Origin</c>, as a CLI client
/// sends, passes the origin check. The body is buffered up to the limit, so a chunked or compressed body is bounded too, not
/// only a declared <c>Content-Length</c>.
/// </remarks>
/// <param name="next">The endpoint's own request delegate.</param>
public sealed class McpRequestGuardMiddleware(RequestDelegate next)
{
    private const int ChunkBytes = 16 * 1024;

    /// <summary>Screens one request, then passes it on or answers it.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task completing when the request is answered or passed on.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        McpRequestGuardOptions options = context.RequestServices.GetRequiredService<IOptionsMonitor<McpRequestGuardOptions>>().CurrentValue;

        var origin = context.Request.Headers.Origin;
        if (origin.Count > 0 && !McpOriginExtensions.Allows(origin.ToString(), OwnOrigin(context.Request), options.AllowedOrigins))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                detail: "This origin may not call the MCP endpoint.").ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (options.MaxRequestBodyBytes is { } limit && !await BufferBodyAsync(context, limit).ConfigureAwait(false))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                detail: $"The request body exceeds the MCP endpoint's {limit} byte limit.").ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    // Forwarded headers have already applied, so this is the address the client used, such as a tailnet host name.
    private static string OwnOrigin(HttpRequest request) => request.Scheme + "://" + request.Host.ToUriComponent();

    private static async Task<bool> BufferBodyAsync(HttpContext context, long limit)
    {
        HttpRequest request = context.Request;
        if (request.ContentLength > limit)
        {
            return false;
        }

        var body = new MemoryStream();
        context.Response.RegisterForDispose(body);
        byte[] chunk = new byte[ChunkBytes];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > limit)
            {
                return false;
            }

            await body.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false);
        }

        body.Position = 0;
        request.Body = body;
        return true;
    }
}
