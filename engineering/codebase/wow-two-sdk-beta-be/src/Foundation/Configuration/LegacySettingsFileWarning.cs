using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

/// <summary>Warns once at startup that a deployed host read the legacy settings file in place of the target's.</summary>
/// <param name="logger">The logger the warning goes to.</param>
internal sealed partial class LegacySettingsFileWarning(ILogger<LegacySettingsFileWarning> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Warn(logger, SettingsFileExtensions.LocalFile, SettingsFileExtensions.TargetFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Read the legacy settings file {LegacyFile}: no {TargetFile} is "
        + "mounted, so the release bundle predates pipelines v0.7.0. Publish on pipelines v0.7.0 or later; a later be sdk "
        + "release drops this fallback.")]
    private static partial void Warn(ILogger logger, string legacyFile, string targetFile);
}
