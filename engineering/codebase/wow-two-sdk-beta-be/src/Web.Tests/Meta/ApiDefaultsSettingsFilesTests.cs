using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Meta;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Meta;

/// <summary><c>AddApiDefaults</c> reads the target's settings file before its options callback, so a product needs no code
/// to read the file a release bundle mounts.</summary>
public sealed class ApiDefaultsSettingsFilesTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("wow2-api-defaults-settings-");

    [Fact]
    public void AddApiDefaults_ReadsTheTargetFile_BeforeItsOptionsCallback()
    {
        File.WriteAllText(Path.Combine(_root.FullName, SettingsFileExtensions.TargetFile),
            """{ "Deployment": { "TrustedProxies": [ "10.0.0.2" ] } }""");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _root.FullName,
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration[LocalRunExtensions.DeployEnvironmentVariable] = "prod";
        string? seen = null;

        builder.AddApiDefaults(options =>
        {
            options.EnableOtlpExporters = false;
            seen = builder.Configuration["Deployment:TrustedProxies:0"];
        });

        seen.Should().Be("10.0.0.2");
        builder.Configuration[LocalRunExtensions.DeployEnvironmentVariable].Should().Be("prod", "an indexer value survives the insert");
    }

    /// <inheritdoc />
    public void Dispose() => _root.Delete(recursive: true);
}
