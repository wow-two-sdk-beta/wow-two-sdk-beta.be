using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>A set that owns its schema claims it with a marker row, and no other owner or set may then migrate it.</summary>
public sealed class SchemaOwnerTests : SqliteMigratorTestBase
{
    [Fact]
    public async Task ApplyPending_ShouldRecordTheOwner_WhenTheSchemaIsUnclaimed()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var migrator = CreateOwned("module");
        (await migrator.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        await using var host = MigrationSetHost.Sqlite(ConnectionString, _ => { });
        (await host.ReadOwnersAsync(MigrationConstants.SchemaOwnerTableName))
            .Should().Equal(("identity", MigrationConstants.DefaultSetName, "module", migrator.Options.OwnerInstance!, "deploy"));
        var status = (await migrator.Runner.GetStatusAsync()).ValueOrThrow();
        status.SchemaOwner!.Owner.Should().Be("module");
    }

    [Fact]
    public async Task ApplyPending_ShouldRefuseBeforeAnyScriptRuns_WhenAnotherOwnerHoldsTheSchema()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var module = CreateOwned("module"))
            (await module.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.Write("002-sessions", "create table identity_sessions(id integer primary key);", "drop table identity_sessions;");
        await using var service = CreateOwned("service");
        var result = await service.Runner.ApplyPendingAsync("deploy");

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        error.Message.Should().Contain("module").And.Contain("service");
        (await service.HasTableAsync("identity_sessions")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPending_ShouldRefuse_WhenASetWithoutAnOwnerMeetsAClaimedSchema()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var module = CreateOwned("module"))
            (await module.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.Write("002-sessions", "create table identity_sessions(id integer primary key);", "drop table identity_sessions;");
        await using var unclaimed = CreateMigrator(options => options.SchemaName = "identity");
        var result = await unclaimed.Runner.ApplyPendingAsync("deploy");

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        (await unclaimed.HasTableAsync("identity_sessions")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPending_ShouldProceed_WhenTheSameOwnerReturns()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var first = CreateOwned("module"))
            (await first.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.Write("002-sessions", "create table identity_sessions(id integer primary key);", "drop table identity_sessions;");
        await using var second = CreateOwned("module");

        (await second.Runner.ApplyPendingAsync("deploy")).ValueOrThrow().Should().Equal("002-sessions");
    }

    [Fact]
    public async Task DryRun_ShouldLeaveTheSchemaUnclaimed()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var migrator = CreateOwned("module");
        (await migrator.Runner.DryRunPendingAsync()).ValueOrThrow().Passed.Should().BeTrue();

        (await migrator.HasTableAsync(MigrationConstants.SchemaOwnerTableName)).Should().BeFalse();
    }

    private Testing.Data.Migrations.MigratorHarness CreateOwned(string owner) =>
        CreateMigrator(options =>
        {
            options.SchemaName = "identity";
            options.SchemaOwner = owner;
        });
}
