using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Runs the <c>config check</c> checks at a boot, before any startup work: a deployed boot refuses on a finding, a local run logs it and serves.</summary>
/// <remarks>
///   - deployed means <c>DEPLOY_ENVIRONMENT</c> is set (<see cref="LocalRunExtensions.IsDeployed"/>)
///   - every finding is logged by setting name, never by value; a stranding refusal logs <c>Critical</c> with the count and the way out
/// </remarks>
internal static partial class ConfigCheckBoot
{
    /// <summary>Holds the log category of the boot check.</summary>
    public const string Category = "WoW.Two.Sdk.Beta.Be.ConfigCheck";

    /// <summary>Runs the checks and decides whether the host may serve.</summary>
    /// <param name="services">The built host's root provider.</param>
    /// <param name="service">The service name.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Null when the host may serve; otherwise the exit code the process ends with.</returns>
    public static async Task<int?> RunAsync(IServiceProvider services, string service, CancellationToken ct)
    {
        var run = await ConfigCheckRunner.RunAsync(services, service, ct).ConfigureAwait(false);
        var result = run.Result;
        if (result.Exit == 0)
            return null;

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(Category) ?? NullLogger.Instance;
        var deployed = services.GetService<IConfiguration>()?.IsDeployed() ?? false;

        if (result.Error is { } error)
            LogFailed(logger, error);
        foreach (var invalid in result.Invalid ?? [])
            LogInvalid(logger, invalid.Setting, invalid.Code, invalid.Message);
        foreach (var key in result.Unknown ?? [])
            LogUnknown(logger, key);
        foreach (var stranded in result.Stranded ?? [])
        {
            if (deployed)
                LogStrandingRefused(logger, stranded.Setting, stranded.Code, stranded.Count);
            else
                LogStranding(logger, stranded.Setting, stranded.Code, stranded.Count);
        }

        foreach (var note in run.Notes)
            LogNote(logger, note);

        if (deployed)
        {
            LogRefused(logger, result.Exit);
            return result.Exit;
        }

        LogLocalStart(logger, result.Exit);
        return null;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Config check could not finish: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Setting {Setting} is invalid ({Code}): {Rule}")]
    private static partial void LogInvalid(ILogger logger, string setting, string code, string rule);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Setting key {Key} is bound by no settings record; remove or rename it")]
    private static partial void LogUnknown(ILogger logger, string key);

    [LoggerMessage(EventId = 4, Level = LogLevel.Critical,
        Message = "Setting {Setting} strands {Count} people ({Code}); restore the setting, or give them another way in first")]
    private static partial void LogStrandingRefused(ILogger logger, string setting, string code, long count);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Setting {Setting} strands {Count} people ({Code}); a deployed boot refuses it")]
    private static partial void LogStranding(ILogger logger, string setting, string code, long count);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Config check: {Note}")]
    private static partial void LogNote(ILogger logger, string note);

    [LoggerMessage(EventId = 7, Level = LogLevel.Critical, Message = "The deployed boot refuses to serve: config check exit {Exit}")]
    private static partial void LogRefused(ILogger logger, int exit);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "A local run serves despite config check exit {Exit}; a deployed boot refuses")]
    private static partial void LogLocalStart(ILogger logger, int exit);
}
