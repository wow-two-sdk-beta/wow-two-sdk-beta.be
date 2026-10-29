using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Configuration;

/// <summary>
/// Covers <see cref="LocalRunExtensions.IsLocalRun(IHostEnvironment, IConfiguration)"/>: only <c>Development</c> without
/// <c>DEPLOY_ENVIRONMENT</c> is local; deployed dev and Production never are, and <c>LOCAL_SETTINGS</c> plays no part.
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
        string environment, string? deployEnvironment, string? localSettings, bool expected)
    {
        var configuration = Configuration(deployEnvironment, localSettings);

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

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    [InlineData("dev", true, "dev")]
    [InlineData(" Prod ", true, "prod")]
    [InlineData("preview-7", true, "preview-7")]
    public void IsDeployed_AndGetDeployEnvironment_ReadTheRunnersVariable(string? deployEnvironment, bool deployed, string? expected)
    {
        var configuration = Configuration(deployEnvironment, localSettings: null);

        configuration.IsDeployed().Should().Be(deployed);
        configuration.GetDeployEnvironment().Should().Be(expected);
    }

    private static IConfiguration Configuration(string? deployEnvironment, string? localSettings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [LocalRunExtensions.DeployEnvironmentVariable] = deployEnvironment,
                [SettingsFileExtensions.LocalSettingsVariable] = localSettings,
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
            empty.IsDeployed().Should().BeTrue();
            empty.GetDeployEnvironment().Should().Be("dev");

            Environment.SetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable, null);
            development.IsLocalRun(empty).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable, previous);
        }
    }
}
