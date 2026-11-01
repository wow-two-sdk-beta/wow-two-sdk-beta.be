using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

namespace WoW.Two.Sdk.Beta.Be.Meta;

/// <summary>Runs a service's <c>Program.cs</c>: answers a one-off verb and exits with its code, or boots and serves.</summary>
/// <remarks>
///   - the first argument picks the path: a pre-host verb (<see cref="ServiceVerbs.AddPreHostVerb(string, Func{ServiceVerbInvocation, int})"/>)
///     runs before any builder exists; a host verb (<c>migrate</c>, <c>config</c> or <see cref="ServiceVerbs.AddHostVerb"/>) runs after
///     <c>Build()</c> and before <c>Configure</c>; any other first argument, or none, serves
///   - a verb's exit code is the process's; a known verb with wrong arguments answers its own usage error and never serves
///   - a host verb's stdout carries only the verb's output: the host's console logs go to stderr for the rest of the process
///   - a serving boot runs the <c>config check</c> checks before <c>Configure</c>: a deployed boot (<c>DEPLOY_ENVIRONMENT</c> set) refuses
///     on a finding and exits with the check's code, a local run logs the findings and serves
/// </remarks>
/// <example>
/// <code>
/// return await ServiceHost.RunAsync(args,
///     builder => builder.Configure(),
///     app => app.ConfigureAsync(),
///     verbs => verbs.AddPreHostVerb("keys", verb => SigningKeyCommand.Run(verb.Arguments, verb.Output, verb.Error, TimeProvider.System)));
///
/// public partial class Program;
/// </code>
/// </example>
public static class ServiceHost
{
    /// <summary>Answers a verb, or builds, configures and serves the host until shutdown.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="configureBuilder">Configures the application builder (services), as the host's <c>Configure(WebApplicationBuilder)</c>.</param>
    /// <param name="configureApp">Runs startup tasks, then configures middleware and endpoints; only a serving boot calls it.</param>
    /// <param name="addVerbs">Adds the service's own verbs; null keeps the built-in ones only.</param>
    /// <returns>The exit code: the verb's, a deployed boot's refusal, or <c>0</c> after a clean shutdown.</returns>
    public static Task<int> RunAsync(
        string[] args, Action<WebApplicationBuilder> configureBuilder, Action<WebApplication> configureApp, Action<ServiceVerbs>? addVerbs = null)
    {
        ArgumentNullException.ThrowIfNull(configureApp);

        return RunAsync(args, configureBuilder, app =>
        {
            configureApp(app);
            return Task.CompletedTask;
        }, addVerbs);
    }

    /// <summary>Answers a verb, or builds, configures (asynchronously) and serves the host until shutdown.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="configureBuilder">Configures the application builder (services), as the host's <c>Configure(WebApplicationBuilder)</c>.</param>
    /// <param name="configureApp">Runs startup tasks, then configures middleware and endpoints, as the host's <c>ConfigureAsync(WebApplication)</c>.</param>
    /// <param name="addVerbs">Adds the service's own verbs; null keeps the built-in ones only.</param>
    /// <returns>The exit code: the verb's, a deployed boot's refusal, or <c>0</c> after a clean shutdown.</returns>
    public static Task<int> RunAsync(
        string[] args, Action<WebApplicationBuilder> configureBuilder, Func<WebApplication, Task> configureApp, Action<ServiceVerbs>? addVerbs = null) =>
        RunCoreAsync(args, configureBuilder, configureApp, addVerbs, Console.Out, Console.Error, isProcess: true, CancellationToken.None);

    /// <summary>Answers a verb or serves, writing every verb's output to the given streams instead of the process console.</summary>
    /// <remarks>For tests and an image's E2E tier: the process console is never redirected, <paramref name="ct"/> stops a serving host, and SIGINT and SIGTERM are left to the caller.</remarks>
    /// <param name="args">The process arguments.</param>
    /// <param name="configureBuilder">Configures the application builder (services).</param>
    /// <param name="configureApp">Runs startup tasks, then configures middleware and endpoints; only a serving boot calls it.</param>
    /// <param name="addVerbs">Adds the service's own verbs; null keeps the built-in ones only.</param>
    /// <param name="output">Standard output for a verb.</param>
    /// <param name="error">Standard error for a verb.</param>
    /// <param name="ct">Token to cancel a verb or to stop a serving host.</param>
    /// <returns>The exit code: the verb's, a deployed boot's refusal, or <c>0</c> after a clean shutdown.</returns>
    public static Task<int> RunAsync(
        string[] args,
        Action<WebApplicationBuilder> configureBuilder,
        Func<WebApplication, Task> configureApp,
        Action<ServiceVerbs>? addVerbs,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return RunCoreAsync(args, configureBuilder, configureApp, addVerbs, output, error, isProcess: false, ct);
    }

    private static async Task<int> RunCoreAsync(
        string[] args,
        Action<WebApplicationBuilder> configureBuilder,
        Func<WebApplication, Task> configureApp,
        Action<ServiceVerbs>? addVerbs,
        TextWriter output,
        TextWriter error,
        bool isProcess,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configureBuilder);
        ArgumentNullException.ThrowIfNull(configureApp);

        var verbs = new ServiceVerbs();
        addVerbs?.Invoke(verbs);

        if (args is [var name, .. var arguments])
        {
            var invocation = new ServiceVerbInvocation(name, arguments, output, error);
            if (verbs.TryGetPreHost(name, out var preHost))
                return await preHost(invocation, ct).ConfigureAwait(false);

            if (HostVerb(name, verbs, isProcess) is { } hostVerb)
            {
                // The host's own loggers write to Console.Out; point it at stderr before the builder exists, so stdout stays the verb's.
                if (isProcess)
                    Console.SetOut(Console.Error);

                var app = Build(args, configureBuilder);
                await using (app.ConfigureAwait(false))
                    return await hostVerb(app.Services, invocation, ct).ConfigureAwait(false);
            }
        }

        var serving = Build(args, configureBuilder);
        if (await ConfigCheckBoot.RunAsync(serving.Services, ServiceName(serving.Services), ct).ConfigureAwait(false) is { } refused)
        {
            await serving.DisposeAsync().ConfigureAwait(false);
            return refused;
        }

        await configureApp(serving).ConfigureAwait(false);
        await serving.RunAsync(ct).ConfigureAwait(false);
        return 0;
    }

    private static Func<IServiceProvider, ServiceVerbInvocation, CancellationToken, Task<int>>? HostVerb(string name, ServiceVerbs verbs, bool isProcess) =>
        name switch
        {
            MigrateCommand.Name => (services, invocation, ct) =>
                MigrateCommand.RunAsync(services, [MigrateCommand.Name, .. invocation.Arguments], invocation.Output, invocation.Error, handleTermination: isProcess, ct),
            ConfigCheckCommand.Name => (services, invocation, ct) =>
                ConfigCheckCommand.RunAsync(services, ServiceName(services), [ConfigCheckCommand.Name, .. invocation.Arguments], invocation.Output, invocation.Error, ct),
            _ => verbs.TryGetHost(name, out var run) ? run : null,
        };

    private static WebApplication Build(string[] args, Action<WebApplicationBuilder> configureBuilder)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder(builder);
        return builder.Build();
    }

    /// <summary>Names the service as the API defaults do: <see cref="ApiDefaultsOptions.ServiceName"/>, else the application name.</summary>
    private static string ServiceName(IServiceProvider services) =>
        services.GetService<ApiDefaultsOptions>()?.ServiceName is { Length: > 0 } name
            ? name
            : services.GetRequiredService<IHostEnvironment>().ApplicationName;
}
