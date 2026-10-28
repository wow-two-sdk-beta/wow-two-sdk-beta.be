using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The read-only level check compares a set's applied level with what the running code expects.</summary>
public sealed class MigrationLevelTests : SqliteMigratorTestBase
{
    [Fact]
    public async Task CheckLevel_ShouldReportBehindWithoutCreatingAnything_WhenTheDatabaseIsFresh()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var migrator = CreateMigrator(options => options.SchemaOwner = "module");
        var report = (await migrator.Runner.CheckLevelAsync()).ValueOrThrow();

        report.State.Should().Be(MigrationLevelState.Behind);
        report.ExpectedLevel.Should().Be(1);
        report.AppliedLevel.Should().Be(0);
        report.Pending.Should().Equal("001-users");
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
        (await migrator.HasTableAsync(MigrationConstants.SchemaOwnerTableName)).Should().BeFalse();
    }

    [Fact]
    public async Task CheckLevel_ShouldReportCurrent_AfterApply()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();
        var report = (await migrator.Runner.CheckLevelAsync()).ValueOrThrow();

        report.IsCurrent.Should().BeTrue();
        report.Set.Should().Be(MigrationConstants.DefaultSetName);
        report.AppliedLevel.Should().Be(1);
    }

    [Fact]
    public async Task CheckLevel_ShouldReportCompatibleAhead_WhenTheDatabaseIsNewerByAnExpand()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        Workspace.Write("002-sessions", "create table identity_sessions(id integer primary key);", "drop table identity_sessions;");
        await using (var newer = CreateMigrator())
            (await newer.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.DeleteFolder("002-sessions");
        await using var older = CreateMigrator();
        var report = (await older.Runner.CheckLevelAsync()).ValueOrThrow();

        report.State.Should().Be(MigrationLevelState.AheadCompatible);
        report.MayRun.Should().BeTrue();
        report.AppliedLevel.Should().Be(2);
        report.ExpectedLevel.Should().Be(1);
        report.Describe().Should().Contain("newer");
    }

    [Fact]
    public async Task CheckLevel_ShouldReportDrifted_WhenAnAppliedScriptChanged()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        Workspace.OverwriteApply("001-users", "create table identity_users(id integer primary key, email text);");

        (await migrator.Runner.CheckLevelAsync()).ValueOrThrow().State.Should().Be(MigrationLevelState.Drifted);
    }

    [Fact]
    public async Task CheckLevel_ShouldReportOwnerConflict_WhenAnotherOwnerHoldsTheSchema()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var module = CreateMigrator(options => options.SchemaOwner = "module"))
            (await module.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        await using var service = CreateMigrator(options => options.SchemaOwner = "service");
        var report = (await service.Runner.CheckLevelAsync()).ValueOrThrow();

        report.State.Should().Be(MigrationLevelState.OwnerConflict);
        report.SchemaOwner!.Owner.Should().Be("module");
    }

    [Fact]
    public async Task VerifyOnStartup_ShouldThrow_WhenASetIsBehindUnderTheFailPolicy()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var host = CreateHost(MigrationLevelPolicy.Fail);
        var verify = () => host.Services.VerifyMigrationLevelsOnStartupAsync();

        (await verify.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*identity*001-users*");
    }

    [Fact]
    public async Task VerifyOnStartup_ShouldWarnAndContinue_WhenASetIsBehindUnderTheWarnPolicy()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var host = CreateHost(MigrationLevelPolicy.Warn);
        await host.Services.VerifyMigrationLevelsOnStartupAsync();

        host.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("identity"));
    }

    [Fact]
    public async Task VerifyOnStartup_ShouldThrowOnAnOwnerConflict_EvenUnderTheWarnPolicy()
    {
        Workspace.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        await using (var module = CreateHost(MigrationLevelPolicy.Fail, owner: "module"))
            (await module.Services.ApplyMigrationSetsAsync("deploy")).ValueOrThrow();

        await using var service = CreateHost(MigrationLevelPolicy.Warn, owner: "service");
        var verify = () => service.Services.VerifyMigrationLevelsOnStartupAsync();

        await verify.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VerifyOnStartup_ShouldPass_WhenTheMigratorIsDisabled()
    {
        await using var host = MigrationSetHost.Sqlite(ConnectionString, services =>
            services.AddDatabaseBespokeMigrations(Workspace.Root, options => options.Provider = DatabaseProvider.Sqlite)
                .DisableBespokeMigrator());

        await host.Services.VerifyMigrationLevelsOnStartupAsync();
    }

    private MigrationSetHost CreateHost(MigrationLevelPolicy policy, string? owner = null) =>
        MigrationSetHost.Sqlite(ConnectionString, services =>
            services.AddDatabaseBespokeMigrationSet("identity", Workspace.Root, options =>
            {
                options.Provider = DatabaseProvider.Sqlite;
                options.SchemaName = "identity";
                options.SchemaOwner = owner;
                options.LevelMismatch = policy;
            }));
}
