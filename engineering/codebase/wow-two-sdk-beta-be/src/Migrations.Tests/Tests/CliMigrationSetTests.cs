using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The <c>wow-migrate</c> deploy step runs one named set or every set in a manifest.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class CliMigrationSetTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture), IAsyncLifetime, IDisposable
{
    private readonly MigrationsWorkspace _identity = new();
    private readonly string _schema = "identity_" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Apply_ShouldRunEverySetInTheManifest()
    {
        Workspace.Write("001-orders", "create table cli_orders(id integer primary key);", "drop table cli_orders;");
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");
        var manifest = WriteManifest(
            new { name = "default", sqlDir = Workspace.Root },
            new { name = "identity", sqlDir = _identity.Root, schema = _schema, schemaOwner = "service" });

        var result = await CliProcess.RunAsync("apply", "--connection", Fixture.ConnectionString, "--sets", manifest);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        result.StandardOutput.Should().Contain("[identity]").And.Contain("001-users");
        await using var host = MigrationSetHost.Postgres(Fixture.ConnectionString, _ => { });
        (await host.ReadHistoryAsync("public.migration_history")).Should().Equal("001-orders");
        (await host.ReadHistoryAsync($"\"{_schema}\".identity_migration_history")).Should().Equal("001-users");
    }

    [Fact]
    public async Task Apply_ShouldRunOnlyTheSelectedSet_WhenSetIsGiven()
    {
        Workspace.Write("001-orders", "create table cli_orders(id integer primary key);", "drop table cli_orders;");
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");
        var manifest = WriteManifest(
            new { name = "default", sqlDir = Workspace.Root },
            new { name = "identity", sqlDir = _identity.Root, schema = _schema, schemaOwner = "service" });

        var result = await CliProcess.RunAsync(
            "apply", "--connection", Fixture.ConnectionString, "--sets", manifest, "--set", "identity");

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        await using var host = MigrationSetHost.Postgres(Fixture.ConnectionString, _ => { });
        (await host.HasTableAsync(_schema, "identity_users")).Should().BeTrue();
        (await host.HasTableAsync("public", "cli_orders")).Should().BeFalse();
    }

    [Fact]
    public async Task Check_ShouldExitOneWhileASetIsBehind_AndZeroOnceApplied()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");
        string[] set = ["--connection", Fixture.ConnectionString, "--sql-dir", _identity.Root, "--set", "identity", "--schema", _schema, "--schema-owner", "service"];

        var behind = await CliProcess.RunAsync(["check", .. set]);
        behind.ExitCode.Should().Be(1, behind.StandardOutput + behind.StandardError);
        behind.StandardError.Should().Contain("001-users");

        (await CliProcess.RunAsync(["apply", .. set])).ExitCode.Should().Be(0);

        var current = await CliProcess.RunAsync(["check", .. set]);
        current.ExitCode.Should().Be(0, current.StandardOutput + current.StandardError);
        current.StandardOutput.Should().Contain("identity").And.Contain("current");
    }

    [Fact]
    public async Task Apply_ShouldReadEmbeddedScripts_WhenTheManifestNamesAnAssembly()
    {
        var manifest = WriteManifest(new
        {
            name = "identity",
            assembly = typeof(CliMigrationSetTests).Assembly.Location,
            resourcePrefix = "SetMigrations/identity/",
            schema = _schema,
            schemaOwner = "service",
        });

        var result = await CliProcess.RunAsync("apply", "--connection", Fixture.ConnectionString, "--sets", manifest);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        await using var host = MigrationSetHost.Postgres(Fixture.ConnectionString, _ => { });
        (await host.HasTableAsync(_schema, "identity_embedded_users")).Should().BeTrue();
    }

    [Fact]
    public async Task Rollback_ShouldExitOne_WhenTheManifestHoldsSeveralSetsAndNoneIsSelected()
    {
        var manifest = WriteManifest(
            new { name = "default", sqlDir = Workspace.Root },
            new { name = "identity", sqlDir = _identity.Root, schema = _schema });

        var result = await CliProcess.RunAsync(
            "rollback", "--connection", Fixture.ConnectionString, "--sets", manifest,
            "--i-understand-this-is", new Npgsql.NpgsqlConnectionStringBuilder(Fixture.ConnectionString).Database!, "--force");

        result.ExitCode.Should().Be(1, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain("--set");
    }

    /// <inheritdoc />
    async Task IAsyncLifetime.DisposeAsync()
    {
        await using (var host = MigrationSetHost.Postgres(Fixture.ConnectionString, _ => { }))
            await host.DropSchemasAsync(_schema);
        await DisposeAsync();
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();

    private string WriteManifest(params object[] sets)
    {
        var path = Path.Combine(Workspace.Root, "migration-sets.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { sets }));
        return path;
    }
}
