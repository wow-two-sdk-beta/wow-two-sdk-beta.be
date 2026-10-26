using AwesomeAssertions;
using Dapper;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>A module's set owns its own PostgreSQL schema beside the product's set in <c>public</c>.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class PostgresMigrationSetTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture), IAsyncLifetime, IDisposable
{
    private readonly MigrationsWorkspace _identity = new();
    private readonly string _schema = "identity_" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task ApplyMigrationSets_ShouldCreateTheOwnedSchemaAndKeepItsHistoryThere()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users",
            "create table {{schema}}.identity_users(id uuid primary key, email text not null);",
            "drop table {{schema}}.identity_users;");

        await using var host = CreateHost("module");
        var applied = (await host.Services.ApplyMigrationSetsAsync("deploy")).ValueOrThrow();

        applied.Select(set => set.Set).Should().Equal(MigrationConstants.DefaultSetName, "identity");
        (await host.HasSchemaAsync(_schema)).Should().BeTrue();
        (await host.HasTableAsync(_schema, "identity_users")).Should().BeTrue();
        (await host.HasTableAsync("public", "identity_users")).Should().BeFalse();
        (await host.ReadHistoryAsync($"\"{_schema}\".\"identity_migration_history\"")).Should().Equal("001-users");
        (await host.ReadHistoryAsync("public.migration_history")).Should().Equal("001-orders");
        (await host.ReadOwnersAsync($"\"{_schema}\".\"{MigrationConstants.SchemaOwnerTableName}\""))
            .Should().Equal((_schema, "identity", "module", "deploy"));
    }

    [Fact]
    public async Task ApplyPending_ShouldRefuse_WhenAnotherOwnerHoldsTheSchema()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");
        await using (var module = CreateHost("module"))
            (await module.Services.ApplyMigrationSetsAsync("deploy", ["identity"])).ValueOrThrow();

        _identity.Write("002-sessions", "create table {{schema}}.identity_sessions(id uuid primary key);", "drop table {{schema}}.identity_sessions;");
        await using var service = CreateHost("service");
        var result = await service.Services.ApplyMigrationSetsAsync("deploy", ["identity"]);

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        (await service.HasTableAsync(_schema, "identity_sessions")).Should().BeFalse();
    }

    [Fact]
    public async Task CheckLevel_ShouldNotCreateTheSchema_WhenTheDatabaseIsFresh()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");

        await using var host = CreateHost("module");
        var reports = (await host.Services.CheckMigrationLevelsAsync(["identity"])).ValueOrThrow();

        reports.Should().ContainSingle().Which.State.Should().Be(MigrationLevelState.Behind);
        (await host.HasSchemaAsync(_schema)).Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_ShouldLeaveNoSchemaBehind()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");

        await using var host = CreateHost("module");
        (await host.Sets.GetRunner("identity").DryRunPendingAsync()).ValueOrThrow().Passed.Should().BeTrue();

        (await host.HasSchemaAsync(_schema)).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPending_ShouldTakeItsOwnAdvisoryLock_WhenTheProductSetIsLocked()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");
        await using var host = CreateHost("module");

        await using var holder = new NpgsqlConnection(Fixture.ConnectionString);
        await holder.OpenAsync();
        var productLock = host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId;
        await holder.ExecuteAsync("select pg_advisory_lock(@productLock)", new { productLock });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            (await host.Sets.GetRunner("identity").ApplyPendingAsync("deploy", timeout.Token))
                .ValueOrThrow().Should().Equal("001-users");
        }
        finally
        {
            await holder.ExecuteAsync("select pg_advisory_unlock(@productLock)", new { productLock });
        }
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

    private MigrationSetHost CreateHost(string owner) =>
        MigrationSetHost.Postgres(Fixture.ConnectionString, services =>
        {
            services.AddDatabaseBespokeMigrations(Workspace.Root);
            services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
            {
                options.SchemaName = _schema;
                options.SchemaOwner = owner;
            });
        });
}
