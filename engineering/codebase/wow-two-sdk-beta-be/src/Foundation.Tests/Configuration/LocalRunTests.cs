using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Configuration;

/// <summary>
/// Covers <see cref="LocalRunExtensions.IsLocalRun(IHostEnvironment, IConfiguration)"/>: only <c>Development</c> without
/// <c>DEPLOY_ENVIRONMENT</c> is local; deployed dev and Production never are, and <c>MACHINE_SETTINGS</c> plays no part.
/// </summary>
public sealed class LocalRunTests
{
    [Theory]
    [InlineData("Development", null, null, true)]
    [InlineData("Development", "", null, true)]
    [InlineData("Development", "   ", null, true)]
    [InlineData("Development", null, "true", true)]
    [InlineData("Development", null, "false", true)]
    [InlineData("Development", "dev", null, false)]
    [InlineData("Development", "dev", "true", false)]
    [InlineData("Development", "prod", null, false)]
    [InlineData("Production", null, null, false)]
    [InlineData("Production", "prod", null, false)]
    [InlineData("Staging", null, null, false)]
    public void IsLocalRun_KeysOnDevelopmentWithoutDeployEnvironment(
        string environment, string? deployEnvironment, string? machineSettings, bool expected)
    {
        var configuration = Configuration(deployEnvironment, machineSettings);

        new TestEnvironment(environment).IsLocalRun(configuration).Should().Be(expected);
    }

    [Fact]
    public void IsLocalRun_OnABuilder_ReadsItsEnvironmentAndConfiguration()
    {
        var local = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        var deployedDev = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        deployedDev.Configuration[LocalRunExtensions.DeployEnvironmentVariable] = "dev";

        local.IsLocalRun().Should().BeTrue();
        deployedDev.IsLocalRun().Should().BeFalse();
    }

    private static IConfiguration Configuration(string? deployEnvironment, string? machineSettings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [LocalRunExtensions.DeployEnvironmentVariable] = deployEnvironment,
                ["MACHINE_SETTINGS"] = machineSettings,
            })
            .Build();

    internal sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "WoW2.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>Holds the one case that sets the process-wide variable, so no parallel test can observe it.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "Process environment";
}

/// <summary>A deployed container stays deployed after the ambient environment sources are removed from configuration.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LocalRunProcessEnvironmentTests
{
    [Fact]
    public void IsLocalRun_ReadsDeployEnvironmentFromTheProcess_WhenConfigurationLacksIt()
    {
        var previous = Environment.GetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable);
        var empty = new ConfigurationBuilder().Build();
        var development = new LocalRunTests.TestEnvironment(Environments.Development);
        try
        {
            Environment.SetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable, "dev");
            development.IsLocalRun(empty).Should().BeFalse();

            Environment.SetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable, null);
            development.IsLocalRun(empty).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable, previous);
        }
    }
}
