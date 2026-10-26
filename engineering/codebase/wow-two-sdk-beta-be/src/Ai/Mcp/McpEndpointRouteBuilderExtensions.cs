using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace WoW.Two.Sdk.Beta.Be.Ai.Mcp;

/// <summary>Maps MCP endpoints protected by a named host policy and screened by the MCP request guard.</summary>
public static class McpEndpointRouteBuilderExtensions
{
    /// <summary>Maps the stateless MCP transport, requires the supplied authorization policy and screens every request with
    /// <see cref="McpRequestGuardMiddleware"/>: a foreign browser origin gets 403 and an oversized body 413.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The MCP endpoint path.</param>
    /// <param name="policy">The registered authorization policy.</param>
    /// <returns>The endpoint builder for further explicit conventions.</returns>
    /// <remarks>The guard reads <see cref="McpRequestGuardOptions"/>, set through
    /// <see cref="McpServiceCollectionExtensions.AddStatelessMcpServer"/> or <c>services.Configure&lt;McpRequestGuardOptions&gt;</c>.</remarks>
    public static IEndpointConventionBuilder MapAuthenticatedMcp(
        this IEndpointRouteBuilder endpoints, string pattern, string policy)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        IEndpointConventionBuilder builder = endpoints.MapMcp(pattern).RequireAuthorization(policy);
        builder.Add(endpoint =>
        {
            if (endpoint.RequestDelegate is { } transport)
            {
                endpoint.RequestDelegate = new McpRequestGuardMiddleware(transport).InvokeAsync;
            }
        });
        return builder;
    }
}
