using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The Testing.Data harness builds a host's set beside a module's, so a module's tests apply both.</summary>
public sealed class MigratorHarnessSetTests : SqliteMigratorTestBase, IDisposable
{
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task ApplyAll_ShouldApplyTheHostSetAndTheModuleSet_InRegistrationOrder()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users", "create table {{schema}}.identity_users(id integer primary key);", "drop table {{schema}}.identity_users;");

        await using var migrator = MigratorHarness.CreateSqlite(ConnectionString, sets => sets
            .AddDefault(Workspace.Root)
            .Add("identity", _identity.Root, options =>
            {
                options.SchemaName = "identity";
                options.SchemaOwner = "module";
            }));

        var applied = (await migrator.ApplyAllAsync()).ValueOrThrow();

        applied.Select(set => set.Set).Should().Equal(MigrationConstants.DefaultSetName, "identity");
        migrator.Sets.Names.Should().Equal(MigrationConstants.DefaultSetName, "identity");
        (await migrator.ReadHistoryAsync()).Select(row => row.Name).Should().Equal("orders");
        (await migrator.ReadHistoryAsync("identity")).Select(row => row.Name).Should().Equal("users");
        migrator.OptionsFor("identity").Provider.Should().Be(DatabaseProvider.Sqlite);
        (await migrator.RunnerFor("identity").CheckLevelAsync()).ValueOrThrow().IsCurrent.Should().BeTrue();
        (await migrator.HasTableAsync("identity", "identity_users")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAll_ShouldRunAModuleSetAlone_WhenTheHarnessHasNoDefaultSet()
    {
        _identity.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var migrator = MigratorHarness.CreateSqlite(ConnectionString, sets => sets.Add("identity", _identity.Root));

        (await migrator.ApplyAllAsync("test")).ValueOrThrow().Should().ContainSingle().Which.Set.Should().Be("identity");
        (await migrator.ReadHistoryAsync("identity")).Should().ContainSingle().Which.AppliedBy.Should().Be("test");
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();
}

/// <summary>On PostgreSQL the harness puts a module's set in its own schema, and the fixture's reset drops that schema too.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class PostgresMigratorHarnessSetTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture), IDisposable
{
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task Reset_ShouldDropAModuleSchema_ThatTheHarnessCreated()
    {
        Workspace.Write("001-orders", "create table harness_orders(id integer primary key);", "drop table harness_orders;");
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");

        await using var migrator = MigratorHarness.CreatePostgres(Fixture.ConnectionString, sets => sets
            .AddDefault(Workspace.Root)
            .Add("identity", _identity.Root, options =>
            {
                options.SchemaName = "harness_identity";
                options.SchemaOwner = "module";
            }));
        (await migrator.ApplyAllAsync()).ValueOrThrow();

        (await migrator.HasTableAsync("harness_identity", "identity_users")).Should().BeTrue();
        (await migrator.ReadHistoryAsync("identity")).Select(row => row.Name).Should().Equal("users");

        await Fixture.ResetAsync();

        (await migrator.HasTableAsync("harness_identity", "identity_users")).Should().BeFalse();
        (await migrator.HasTableAsync("harness_orders")).Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();
}
