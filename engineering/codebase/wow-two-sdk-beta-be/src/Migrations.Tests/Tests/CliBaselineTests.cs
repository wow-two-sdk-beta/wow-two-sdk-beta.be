using AwesomeAssertions;
using Dapper;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary><c>wow-migrate baseline</c> adopts an existing database for one set, behind the target confirmation; an owned set names its instance.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class CliBaselineTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    private string Database => new NpgsqlConnectionStringBuilder(Fixture.ConnectionString).Database!;

    [Fact]
    public async Task Baseline_ShouldRecordWithoutRunning_WhenTheTargetIsConfirmed()
    {
        Workspace.Write("001-users", "create table cli_users(id integer primary key);", "drop table cli_users;");
        Workspace.Write("002-orders", "create table cli_orders(id integer primary key);", "drop table cli_orders;");
        await ExecuteAsync("create table cli_users(id integer primary key);");

        var result = await CliProcess.RunAsync(
            "baseline", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root, "--to", "1", "--confirm", Database);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        result.StandardOutput.Should().Contain("001-users");
        var apply = await CliProcess.RunAsync("apply", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);
        apply.ExitCode.Should().Be(0, apply.StandardOutput + apply.StandardError);
        apply.StandardOutput.Should().Contain("002-orders").And.NotContain("001-users");
        await using var migrator = CreateMigrator();
        (await migrator.ReadHistoryAsync()).Select(row => row.AppliedBy).Should().Equal("baseline", "cli");
    }

    [Fact]
    public async Task Baseline_ShouldExitOne_WithoutTheTargetConfirmation()
    {
        Workspace.Write("001-users", "create table cli_users(id integer primary key);", "drop table cli_users;");

        var result = await CliProcess.RunAsync("baseline", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root);

        result.ExitCode.Should().Be(1, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain($"--confirm {Database}");
        await using var migrator = CreateMigrator();
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_ShouldExitOne_WhenAnOwnedSetNamesNoInstance()
    {
        Workspace.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");

        var result = await CliProcess.RunAsync(
            "apply", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root,
            "--set", "identity", "--schema", "identity", "--schema-owner", "service");

        result.ExitCode.Should().Be(1, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain("--owner-instance");
    }

    [Fact]
    public async Task Apply_ShouldRecordTheInstance_WhenOneIsGiven()
    {
        Workspace.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");

        var result = await CliProcess.RunAsync(
            "apply", "--connection", Fixture.ConnectionString, "--sql-dir", Workspace.Root,
            "--set", "identity", "--schema", "identity", "--schema-owner", "service", "--owner-instance", "identity-haven@prod");

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        (await connection.ExecuteScalarAsync<string>("select instance from identity.migration_schema_owner"))
            .Should().Be("identity-haven@prod");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.ExecuteAsync(sql);
    }
}
