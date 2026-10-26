using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;

namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Registers stateless MCP hosting with per-operation authorization, a request guard and agent-readable tool errors.</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>Registers the HTTP transport, authorization filters and the tool error filter for caller-owned MCP tools.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="guard">Sets the browser origins allowed besides the endpoint's own and the request body limit; validated at start.</param>
    /// <returns>The MCP builder for explicit tool, prompt and resource registration.</returns>
    /// <remarks>
    ///   - a failed tool call returns an <c>isError</c> result holding a <see cref="McpToolError"/> (<see cref="McpToolErrorFilter"/>)
    ///   - the guard applies to every endpoint <see cref="McpEndpointRouteBuilderExtensions.MapAuthenticatedMcp"/> maps
    /// </remarks>
    public static IMcpServerBuilder AddStatelessMcpServer(
        this IServiceCollection services, Action<McpRequestGuardOptions>? guard = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<McpRequestGuardOptions>();
        if (guard is not null)
        {
            options.Configure(guard);
        }

        options
            .Validate(
                value => value.AllowedOrigins.All(origin => McpOriginExtensions.NormalizeOrigin(origin) is not null),
                "McpRequestGuardOptions.AllowedOrigins must list browser origins as scheme://host[:port] over http or https.")
            .Validate(
                value => value.MaxRequestBodyBytes is null or > 0,
                "McpRequestGuardOptions.MaxRequestBodyBytes must be positive, or null for no limit.")
            .ValidateOnStart();

        return services.AddMcpServer()
            .WithHttpTransport(transport =>
            {
                transport.SessionMode = HttpServerSessionMode.Stateless;
            })
            .AddAuthorizationFilters()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpToolErrorFilter.Apply));
    }
}
