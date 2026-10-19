using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

/// <summary>
/// Tells a local run from a deployed host. Deployed dev runs as <c>Development</c>, so <c>IsDevelopment()</c> alone
/// never means the developer's machine: a run is local only in <c>Development</c> without <c>DEPLOY_ENVIRONMENT</c>.
/// </summary>
/// <remarks>
///   - gate every local-only convenience on <see cref="IsLocalRun(IHostEnvironment, IConfiguration)"/>, never on <c>IsDevelopment()</c> alone
///   - <c>DEPLOY_ENVIRONMENT</c> counts when either configuration or the process environment carries it, so removing the
///     ambient environment sources (<see cref="EnvironmentAliasExtensions.UseEnvironmentAliases"/>) cannot turn a deployed host local
///   - <c>MACHINE_SETTINGS</c> plays no part: it only adds <c>appsettings.Machine.json</c> on a local run
/// </remarks>
public static class LocalRunExtensions
{
    /// <summary>The host-bootstrap variable the deploy runner sets on every deployed container, <c>dev</c> or <c>prod</c>.</summary>
    public const string DeployEnvironmentVariable = "DEPLOY_ENVIRONMENT";

    /// <summary>Reports whether the host runs on the developer's machine: <c>Development</c> without <c>DEPLOY_ENVIRONMENT</c>.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">The host configuration, read for <c>DEPLOY_ENVIRONMENT</c> beside the process environment.</param>
    /// <returns><see langword="true"/> on a local run; <see langword="false"/> on any deployed host and outside <c>Development</c>.</returns>
    /// <example>
    /// <code>
    /// if (builder.Environment.IsLocalRun(builder.Configuration))
    ///     builder.Services.AddSingleton&lt;ICodeSender, LogCodeSender&gt;();
    /// </code>
    /// </example>
    public static bool IsLocalRun(this IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        return Evaluate(environment, configuration);
    }

    /// <summary>Reports whether the host being built runs on the developer's machine.</summary>
    /// <param name="builder">The host builder, usually <c>WebApplicationBuilder</c>.</param>
    /// <returns><see langword="true"/> on a local run.</returns>
    public static bool IsLocalRun(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return Evaluate(builder.Environment, builder.Configuration);
    }

    /// <summary>Reports whether the host behind <paramref name="services"/> is a local run; no registered environment is never local.</summary>
    /// <param name="services">The provider the SDK's own seams resolve from.</param>
    /// <returns><see langword="true"/> on a local run.</returns>
    internal static bool IsLocalRun(IServiceProvider services)
        => services.GetService<IHostEnvironment>() is { } environment
            && Evaluate(environment, services.GetService<IConfiguration>());

    private static bool Evaluate(IHostEnvironment environment, IConfiguration? configuration)
        => environment.IsDevelopment()
            && string.IsNullOrWhiteSpace(configuration?[DeployEnvironmentVariable])
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DeployEnvironmentVariable));
}
