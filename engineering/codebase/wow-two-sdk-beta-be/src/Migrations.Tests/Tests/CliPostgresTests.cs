using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>Verifies the <c>wow-migrate</c> process against a real PostgreSQL database.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class CliPostgresTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    [Fact]
    public async Task Apply_ShouldExitTwoAndNameTheRolledBackBatch_WhenAMigrationFails()
    {
        Workspace.Write("001-items", "create table cli_item(id integer primary key);", "drop table cli_item;");
        Workspace.Write("002-broken", "alter table cli_missing add column name text;", "-- nothing to undo");

        var result = await CliProcess.RunAsync("apply", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);

        result.ExitCode.Should().Be(2, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain(
            "Migration 002-broken failed; rolled back 001-items, 002-broken, so none of them is recorded.");
        await using var migrator = CreateMigrator();
        (await migrator.HasTableAsync("cli_item")).Should().BeFalse();
        (await migrator.ReadHistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyDryRun_ShouldExitTwoAndLeaveTheSchemaUntouched_WhenAMigrationFails()
    {
        Workspace.Write("001-items", "create table cli_dry_item(id integer primary key);", "drop table cli_dry_item;");
        Workspace.Write("002-broken", "alter table cli_dry_missing add column name text;", "-- nothing to undo");

        var result = await CliProcess.RunAsync(
            "apply", "--dry-run", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);

        result.ExitCode.Should().Be(2, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain("rehearsal failed at 002-broken");
        MigrateRunResult.FromOutput(result.StandardOutput)!.Sets.Single().Pending.Select(label => label.Label).Should().Equal("001-items", "002-broken");
        await using var migrator = CreateMigrator();
        (await migrator.HasTableAsync("cli_dry_item")).Should().BeFalse();
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
    }

    [Fact]
    public async Task Check_ShouldExitZero_OnACompatibleAhead()
    {
        Workspace.Write("001-items", "create table cli_ahead_item(id integer primary key);", "drop table cli_ahead_item;");
        Workspace.Write("002-more", "create table cli_ahead_more(id integer primary key);", "drop table cli_ahead_more;");
        (await CliProcess.RunAsync("apply", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root)).ExitCode.Should().Be(0);
        Workspace.DeleteFolder("002-more");

        var result = await CliProcess.RunAsync("check", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        MigrateRunResult.FromOutput(result.StandardOutput)!.Sets.Single().State.Should().Be(MigrationLevelState.AheadCompatible);
    }

    [Fact]
    public async Task ApplyDryRun_ShouldExitZero_WhenEveryMigrationSucceeds()
    {
        Workspace.Write("001-items", "create table cli_pass_item(id integer primary key);", "drop table cli_pass_item;");

        var result = await CliProcess.RunAsync(
            "apply", "--dry-run", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain("rehearsed 1 of 1 and rolled back");
        await using var migrator = CreateMigrator();
        (await migrator.HasTableAsync("cli_pass_item")).Should().BeFalse();
    }
}
