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
}
