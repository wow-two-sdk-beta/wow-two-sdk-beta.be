using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Configuration;

/// <summary>
/// Covers <see cref="SettingsFileExtensions.AddSettingsFiles{TBuilder}"/>: the target's mounted file follows
/// <c>appsettings.{Environment}.json</c>, the machine's own file follows it on a local run that asks, environment
/// variables still win, and a deployed host without the target file reads the legacy one with a warning.
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("wow2-settings-files-");

    public SettingsFileTests()
    {
        Write("appsettings.json", """{ "Probe": { "Layer": "base", "Shared": "base" } }""");
        Write("appsettings.Development.json", """{ "Probe": { "Layer": "development" } }""");
        Write("appsettings.Production.json", """{ "Probe": { "Layer": "production" } }""");
    }

    [Fact]
    public void TargetFile_FollowsTheEnvironmentFile_AndEnvironmentVariablesStillWin()
    {
        Write(SettingsFileExtensions.TargetFile, """{ "Probe": { "Layer": "target", "Override": "target" } }""");
        var builder = Builder("Production", deployed: "prod");

        builder.AddSettingsFiles();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Probe:Override"] = "explicit" });

        builder.Configuration["Probe:Layer"].Should().Be("target", "the target's file follows appsettings.Production.json");
        builder.Configuration["Probe:Override"].Should().Be("explicit", "every later source still wins over it");
        builder.Configuration["Probe:Shared"].Should().Be("base");
        IndexOf(builder, SettingsFileExtensions.TargetFile).Should().Be(IndexOf(builder, "appsettings.Production.json") + 1)
            .And.BeLessThan(FirstEnvironmentVariablesSource(builder), "environment variables win over the target's file");
    }

    [Fact]
    public void LocalFile_FollowsTheTargetFile_OnALocalRunWhoseProfileAsks()
    {
        Write(SettingsFileExtensions.TargetFile, """{ "Probe": { "Layer": "target" } }""");
        Write(SettingsFileExtensions.LocalFile, """{ "Probe": { "Layer": "local", "Machine": "local" } }""");
        var builder = Builder("Development", localSettings: "true");

        builder.AddSettingsFiles();

        builder.Configuration["Probe:Layer"].Should().Be("local");
        builder.Configuration["Probe:Machine"].Should().Be("local");
        IndexOf(builder, SettingsFileExtensions.LocalFile).Should().Be(IndexOf(builder, SettingsFileExtensions.TargetFile) + 1)
            .And.BeLessThan(FirstEnvironmentVariablesSource(builder));
    }

    [Theory]
    [InlineData("Development", null, null)]      // a test host: the launch profile never asked
    [InlineData("Development", "dev", "true")]   // deployed dev runs as Development; the runner names it
    [InlineData("Production", null, "true")]
    [InlineData("Production", "prod", "true")]
    public void LocalFile_StaysUnread_OutsideALocalRunThatAsks(string environment, string? deployed, string? localSettings)
    {
        Write(SettingsFileExtensions.TargetFile, """{ "Probe": { "Target": "target" } }""");
        Write(SettingsFileExtensions.LocalFile, """{ "Probe": { "Machine": "local" } }""");
        var builder = Builder(environment, deployed, localSettings);

        builder.AddSettingsFiles();

        builder.Configuration["Probe:Machine"].Should().BeNull();
        builder.Configuration["Probe:Target"].Should().Be("target");
        IndexOf(builder, SettingsFileExtensions.LocalFile).Should().Be(-1);
    }

    [Fact]
    public async Task LegacyLocalFile_StandsInForTheTarget_OnADeployedHostWithoutOne()
    {
        Write(SettingsFileExtensions.LocalFile, """{ "Probe": { "Layer": "legacy" } }""");
        var builder = Builder("Production", deployed: "prod");
        var logs = new CapturingLoggerProvider();
        builder.Logging.ClearProviders().AddProvider(logs);

        builder.AddSettingsFiles();

        builder.Configuration["Probe:Layer"].Should().Be("legacy");
        IndexOf(builder, SettingsFileExtensions.LocalFile).Should().Be(IndexOf(builder, "appsettings.Production.json") + 1);
        IndexOf(builder, SettingsFileExtensions.TargetFile).Should().Be(-1);

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
        logs.Warnings.Should().ContainSingle()
            .Which.Should().Contain(SettingsFileExtensions.LocalFile).And.Contain(SettingsFileExtensions.TargetFile);
    }

    [Theory]
    [InlineData("Production", "prod", true)]    // the bundle mounts the target's file: the legacy one is ignored
    [InlineData("Production", null, false)]     // not deployed: a stray machine file never stands in for a target's
    [InlineData("Development", null, false)]
    public async Task LegacyLocalFile_StaysUnread_WithATargetFileOrOffADeployedHost(string environment, string? deployed, bool target)
    {
        if (target)
            Write(SettingsFileExtensions.TargetFile, """{ "Probe": { "Layer": "target" } }""");
        Write(SettingsFileExtensions.LocalFile, """{ "Probe": { "Layer": "legacy" } }""");
        var builder = Builder(environment, deployed);
        var logs = new CapturingLoggerProvider();
        builder.Logging.ClearProviders().AddProvider(logs);

        builder.AddSettingsFiles();

        builder.Configuration["Probe:Layer"].Should().NotBe("legacy");
        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
        logs.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void AddSettingsFiles_IsIdempotent()
    {
        Write(SettingsFileExtensions.TargetFile, """{ "Probe": { "Layer": "target" } }""");
        Write(SettingsFileExtensions.LocalFile, """{ "Probe": { "Layer": "local" } }""");
        var builder = Builder("Development", localSettings: "true");
        var count = builder.Configuration.Sources.Count;

        builder.AddSettingsFiles();
        builder.AddSettingsFiles();

        builder.Configuration.Sources.Count.Should().Be(count + 2);
    }

    [Fact]
    public void AddSettingsFiles_AddsNothing_WithoutAFileToRead()
    {
        var builder = Builder("Development", localSettings: "true");
        var count = builder.Configuration.Sources.Count;

        builder.AddSettingsFiles();

        builder.Configuration.Sources.Count.Should().Be(count);
    }

    [Fact]
    public void IndexerValues_SurviveTheInsert()
    {
        Write(SettingsFileExtensions.TargetFile, """{ "Probe": { "Layer": "target" } }""");
        var builder = Builder("Production", deployed: "prod");
        builder.Configuration["Probe:Indexed"] = "kept";

        builder.AddSettingsFiles();

        builder.Configuration["Probe:Indexed"].Should().Be("kept", "inserting a source rebuilds every provider");
        builder.Configuration[LocalRunExtensions.DeployEnvironmentVariable].Should().Be("prod");
        builder.Configuration["Probe:Layer"].Should().Be("target");
    }

    [Fact]
    public void ReadsLocalFile_ReadsTheSwitchOnALocalRunOnly()
    {
        var local = new LocalRunTests.TestEnvironment(Environments.Development);
        var on = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SettingsFileExtensions.LocalSettingsVariable] = "true" })
            .Build();
        var deployedDev = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SettingsFileExtensions.LocalSettingsVariable] = "true",
                [LocalRunExtensions.DeployEnvironmentVariable] = "dev",
            })
            .Build();

        SettingsFileExtensions.ReadsLocalFile(local, on).Should().BeTrue();
        SettingsFileExtensions.ReadsLocalFile(local, new ConfigurationBuilder().Build()).Should().BeFalse();
        SettingsFileExtensions.ReadsLocalFile(local, deployedDev).Should().BeFalse();
    }

    /// <summary>A host over the throwaway content root; <paramref name="deployed"/> and <paramref name="localSettings"/>
    /// stand in for the runner's and the launch profile's variables, set through configuration.</summary>
    private HostApplicationBuilder Builder(string environment, string? deployed = null, string? localSettings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = _root.FullName,
            EnvironmentName = environment,
        });
        builder.Configuration[LocalRunExtensions.DeployEnvironmentVariable] = deployed;
        builder.Configuration[SettingsFileExtensions.LocalSettingsVariable] = localSettings;
        return builder;
    }

    private static int IndexOf(HostApplicationBuilder builder, string file)
    {
        var sources = builder.Configuration.Sources;
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index] is JsonConfigurationSource { Path: var path } && path == file)
                return index;
        }

        return -1;
    }

    private static int FirstEnvironmentVariablesSource(HostApplicationBuilder builder)
    {
        var sources = builder.Configuration.Sources;
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
                return index;
        }

        return int.MaxValue;
    }

    private void Write(string file, string json) => File.WriteAllText(Path.Combine(_root.FullName, file), json);

    /// <inheritdoc />
    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>Keeps every warning a host logs.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings) return [.. _warnings];
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
        }

        private sealed class Capture(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel != LogLevel.Warning)
                    return;
                lock (owner._warnings) owner._warnings.Add(formatter(state, exception));
            }
        }
    }
}
