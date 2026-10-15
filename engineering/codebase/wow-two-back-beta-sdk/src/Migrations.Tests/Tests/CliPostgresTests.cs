using AwesomeAssertions;
using WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Harness;
using WoW.Two.Sdk.Backend.Beta.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Tests;

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
        result.StandardOutput.Should().Contain("✓ 001-items").And.Contain("✗ 002-broken");
        result.StandardError.Should().Contain("Dry run failed at 002-broken.");
        await using var migrator = CreateMigrator();
        (await migrator.HasTableAsync("cli_dry_item")).Should().BeFalse();
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyDryRun_ShouldExitZero_WhenEveryMigrationSucceeds()
    {
        Workspace.Write("001-items", "create table cli_pass_item(id integer primary key);", "drop table cli_pass_item;");

        var result = await CliProcess.RunAsync(
            "apply", "--dry-run", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        result.StandardOutput.Should().Contain("Dry run passed: 1 rehearsed.");
        await using var migrator = CreateMigrator();
        (await migrator.HasTableAsync("cli_pass_item")).Should().BeFalse();
    }
}
