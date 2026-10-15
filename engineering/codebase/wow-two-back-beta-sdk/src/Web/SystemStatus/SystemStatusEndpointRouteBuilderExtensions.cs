using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Backend.Beta.Web.SystemStatus;

/// <summary>Maps the anonymous system status endpoint that reports liveness and the running build.</summary>
public static class SystemStatusEndpointRouteBuilderExtensions
{
    /// <summary>The route the status endpoint answers on by default.</summary>
    public const string DefaultPattern = "api/system/status";

    /// <summary>
    /// Maps <c>GET <paramref name="pattern"/></c> to <c>{ service, status: "ok", version, commit, builtAt }</c> plus any
    /// <see cref="SystemStatusOptions.Facts"/>. Anonymous, because a sign-in screen reads it before authentication.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route; defaults to <c>api/system/status</c>.</param>
    /// <param name="configure">Service name, version assembly and app-specific facts.</param>
    /// <remarks>The build version is read once, at mapping: <see cref="BuildVersion.Read"/> over the version assembly and the host configuration.</remarks>
    public static RouteHandlerBuilder MapSystemStatus(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern,
        Action<SystemStatusOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var options = new SystemStatusOptions();
        configure?.Invoke(options);
        var assembly = options.VersionAssembly ?? Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("No entry assembly; set SystemStatusOptions.VersionAssembly.");
        var build = BuildVersion.Read(assembly, endpoints.ServiceProvider.GetService<IConfiguration>());
        var service = options.ServiceName ?? assembly.GetName().Name ?? "service";

        return endpoints.MapGet(pattern, (HttpContext context) =>
            {
                var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["service"] = service,
                    ["status"] = "ok",
                    ["version"] = build.Version,
                    ["commit"] = build.Commit,
                    ["builtAt"] = build.BuiltAtUtc,
                };
                foreach (var (key, value) in options.Facts?.Invoke(context) ?? [])
                    payload.TryAdd(key, value);

                return Results.Ok(payload);
            })
            .AllowAnonymous()
            .WithName("SystemStatus");
    }
}
