using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The header grammar and the pending rules: a below-level ordinal applies only locally, and an unmet <c>@requires</c> refuses.</summary>
public sealed class PendingRuleTests : SqliteMigratorTestBase
{
    [Fact]
    public async Task Apply_ShouldRefuseAPendingOrdinalBelowTheLevel_OutsideALocalRun()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        Workspace.Write("003-orders", "create table orders(id integer primary key);", "drop table orders;");
        await using (var first = CreateMigrator(options => options.LocalRun = false))
            (await first.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.Write("002-tags", "create table tags(id integer primary key);", "drop table tags;");
        await using var deployed = CreateMigrator(options => options.LocalRun = false);

        var refused = await deployed.Runner.ApplyPendingAsync("deploy");

        refused.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        error.Message.Should().Contain("002-tags").And.Contain("003");
        (await deployed.HasTableAsync("tags")).Should().BeFalse();
        (await deployed.Runner.PlanAsync()).ValueOrThrow().BelowLevel.Select(migration => migration.Label).Should().Equal("002-tags");
    }

    [Fact]
    public async Task Apply_ShouldApplyAPendingOrdinalBelowTheLevel_OnALocalRun()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        Workspace.Write("003-orders", "create table orders(id integer primary key);", "drop table orders;");
        await using (var first = CreateMigrator(options => options.LocalRun = true))
            (await first.Runner.ApplyPendingAsync("startup")).ValueOrThrow();

        Workspace.Write("002-tags", "create table tags(id integer primary key);", "drop table tags;");
        await using var local = CreateMigrator(options => options.LocalRun = true);

        (await local.Runner.ApplyPendingAsync("startup")).ValueOrThrow().Should().Equal("002-tags");
    }

    [Fact]
    public async Task Apply_ShouldRefuseAFile_WhoseRequiredBackgroundMigrationHasNotFinished()
    {
        Workspace.Write("001-users", "-- @requires backfill_emails\ncreate table users(id integer primary key);", "drop table users;");
        await using var migrator = CreateMigrator();

        var refused = await migrator.Runner.ApplyPendingAsync("deploy");

        refused.IsFailure(out var error, out _).Should().BeTrue();
        error!.Message.Should().Contain("backfill_emails");
        (await migrator.HasTableAsync("users")).Should().BeFalse();
    }

    [Fact]
    public async Task Plan_ShouldReadTheHeader_KindSinceRequiresAndLockTimeout()
    {
        Workspace.Write("001-users", "-- a plain comment\n-- @lock-timeout 12\ncreate table users(id integer primary key);", "drop table users;");
        Workspace.Write("002-drop", "-- @contract since=v0.5\n-- @requires backfill_emails\nalter table users drop column legacy;", "select 1;");
        await using var migrator = CreateMigrator();

        var pending = (await migrator.Runner.PlanAsync()).ValueOrThrow().Pending;

        pending[0].Kind.Should().Be(MigrationKind.Expand);
        pending[0].LockTimeout.Should().Be(TimeSpan.FromSeconds(12));
        pending[1].Kind.Should().Be(MigrationKind.Contract);
        pending[1].ContractSince.Should().Be("v0.5");
        pending[1].Requires.Should().Equal("backfill_emails");
    }

    [Theory]
    [InlineData("-- @contract\ndrop table users;")]
    [InlineData("-- @contract since=soon\ndrop table users;")]
    [InlineData("-- @contracts since=v0.5\ndrop table users;")]
    [InlineData("-- @lock-timeout forever\ndrop table users;")]
    public async Task Plan_ShouldRefuseAMalformedHeader(string applySql)
    {
        Workspace.Write("001-users", applySql, "select 1;");
        await using var migrator = CreateMigrator();

        var plan = await migrator.Runner.PlanAsync();

        plan.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Validation);
    }
}
