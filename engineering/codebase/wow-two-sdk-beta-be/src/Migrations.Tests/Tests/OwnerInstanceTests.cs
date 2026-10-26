using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The owner marker names the instance, so two instances never both claim one schema; replicas of one instance share it.</summary>
public sealed class OwnerInstanceTests : Harness.SqliteMigratorTestBase
{
    [Fact]
    public async Task ApplyPending_ShouldRefuseASecondInstance_WhenAnotherInstanceClaimedTheSchema()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var haven = CreateInstance("identity-haven@prod"))
            (await haven.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.Write("002-sessions", "create table identity_sessions(id integer primary key);", "drop table identity_sessions;");
        await using var foreverPin = CreateInstance("identity-forever-pin@prod");
        var result = await foreverPin.Runner.ApplyPendingAsync("deploy");

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        error.Message.Should().Contain("identity-haven@prod").And.Contain("identity-forever-pin@prod");
        (await foreverPin.HasTableAsync("identity_sessions")).Should().BeFalse();

        var report = (await foreverPin.Runner.CheckLevelAsync()).ValueOrThrow();
        report.State.Should().Be(MigrationLevelState.OwnerConflict);
        report.SchemaOwner!.Instance.Should().Be("identity-haven@prod");
    }

    [Fact]
    public async Task ApplyPending_ShouldProceed_WhenAReplicaOfTheSameInstanceReturns()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var first = CreateInstance("identity-haven@prod"))
            (await first.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.Write("002-sessions", "create table identity_sessions(id integer primary key);", "drop table identity_sessions;");
        await using var replica = CreateInstance("identity-haven@prod");

        (await replica.Runner.ApplyPendingAsync("deploy")).ValueOrThrow().Should().Equal("002-sessions");
        (await replica.Runner.GetStatusAsync()).ValueOrThrow().SchemaOwner!.Instance.Should().Be("identity-haven@prod");
    }

    [Fact]
    public void OwnerInstance_ShouldComeFromConfiguration_WhenTheHostSetsIt()
    {
        var options = ResolveDefaultSet(new() { [MigrationConstants.OwnerInstanceConfigurationKey] = "identity-haven", ["applicationName"] = "Identity.Api" });

        options.OwnerInstance.Should().Be("identity-haven");
    }

    [Fact]
    public void OwnerInstance_ShouldDefaultToTheApplicationAndDeployEnvironment()
    {
        var options = ResolveDefaultSet(new() { ["applicationName"] = "Identity.Api", ["DEPLOY_ENVIRONMENT"] = "prod" });

        options.OwnerInstance.Should().Be("Identity.Api@prod");
    }

    [Fact]
    public void OwnerInstance_ShouldKeepTheCodeValue_OverConfiguration()
    {
        var options = ResolveDefaultSet(
            new() { [MigrationConstants.OwnerInstanceConfigurationKey] = "from-configuration" },
            configure: options => options.OwnerInstance = "from-code");

        options.OwnerInstance.Should().Be("from-code");
    }

    [Fact]
    public void OwnerInstance_ShouldDefaultOnANamedSet()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(new() { ["applicationName"] = "Haven.Api", ["DEPLOY_ENVIRONMENT"] = "dev" }));
        services.AddDatabaseBespokeMigrationSet("identity", Workspace.Root, options => options.SchemaOwner = "module");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<MigrationOptions>("identity").OwnerInstance.Should().Be("Haven.Api@dev");
    }

    private MigratorHarness CreateInstance(string instance) =>
        CreateMigrator(options =>
        {
            options.SchemaName = "identity";
            options.SchemaOwner = "service";
            options.OwnerInstance = instance;
        });

    private MigrationOptions ResolveDefaultSet(Dictionary<string, string?> values, Action<MigrationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(values));
        services.AddDatabaseBespokeMigrations(Workspace.Root, configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<MigrationOptions>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
