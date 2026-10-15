using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Backend.Beta.Web.SystemStatus;
using Xunit;

namespace WoW.Two.Sdk.Backend.Beta.Web.Tests.SystemStatus;

/// <summary>The status endpoint reports the build from assembly metadata, overridable by configuration.</summary>
public sealed class SystemStatusTests
{
    private static readonly AssemblyBuilder Stamped = Probe("1.4.2+3f9c2ab", "2026-10-15T08:30:00Z");

    [Fact]
    public void Read_ShouldSplitTheCommitAndParseTheBuildTime()
    {
        var build = BuildVersion.Read(Stamped);

        build.Should().Be(new BuildVersion
        {
            Version = "1.4.2+3f9c2ab",
            Commit = "3f9c2ab",
            BuiltAtUtc = new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero),
        });
    }

    [Fact]
    public void Read_ShouldLetConfigurationOverrideEachValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Build:Version"] = "2.0.0+feedbee",
                ["Build:Time"] = "2026-10-16T00:00:00Z",
            })
            .Build();

        var build = BuildVersion.Read(Stamped, configuration);

        build.Version.Should().Be("2.0.0+feedbee");
        build.Commit.Should().Be("feedbee");
        build.BuiltAtUtc.Should().Be(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Read_WithoutVersionMetadata_ShouldReportTheAssemblyVersionWithoutACommit()
    {
        var build = BuildVersion.Read(Probe(informationalVersion: null, buildTime: null));

        build.Version.Should().Be("0.0.0.0");
        build.Commit.Should().BeNull();
        build.BuiltAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task MapSystemStatus_ShouldReportTheBuildAndAppFactsAnonymously()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapSystemStatus(configure: o =>
        {
            o.ServiceName = "Console";
            o.VersionAssembly = Stamped;
            o.Facts = _ => [new("localRig", true), new("status", "overridden")];
        });
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync("/api/system/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("service").GetString().Should().Be("Console");
        root.GetProperty("status").GetString().Should().Be("ok");
        root.GetProperty("version").GetString().Should().Be("1.4.2+3f9c2ab");
        root.GetProperty("commit").GetString().Should().Be("3f9c2ab");
        root.GetProperty("builtAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero));
        root.GetProperty("localRig").GetBoolean().Should().BeTrue();
    }

    private static AssemblyBuilder Probe(string? informationalVersion, string? buildTime)
    {
        var attributes = new List<CustomAttributeBuilder>();
        if (informationalVersion is not null)
            attributes.Add(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion]));
        if (buildTime is not null)
            attributes.Add(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, [BuildVersion.BuildTimeMetadataKey, buildTime]));

        return AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"StatusProbe{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run, attributes);
    }
}
