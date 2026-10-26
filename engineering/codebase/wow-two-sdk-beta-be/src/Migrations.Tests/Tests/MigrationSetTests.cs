using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>Several named migration sets in one host keep separate histories, run in registration order and refuse to share a history table.</summary>
public sealed class MigrationSetTests : SqliteMigratorTestBase, IDisposable
{
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task ApplyMigrationSets_ShouldKeepEachSetsHistorySeparate_WhenTwoSetsShareADatabase()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var host = CreateHost();

        host.Sets.Names.Should().Equal(MigrationConstants.DefaultSetName, "identity");

        var applied = (await host.Services.ApplyMigrationSetsAsync("test")).ValueOrThrow();

        applied.Select(set => set.Set).Should().Equal(MigrationConstants.DefaultSetName, "identity");
        applied.Should().AllSatisfy(set => set.Applied.Should().Equal("001-" + (set.Set == "identity" ? "users" : "orders")));
        (await host.ReadHistoryAsync("migration_history")).Should().Equal("001-orders");
        (await host.ReadHistoryAsync("identity_migration_history")).Should().Equal("001-users");
        host.Sets.GetOptions("identity").TableName.Should().Be("identity_migration_history");
        host.Sets.GetOptions("identity").AdvisoryLockId.Should().NotBe(host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId);
    }

    [Fact]
    public async Task ApplyMigrationSets_ShouldRunOnlyTheNamedSets_WhenAFilterIsGiven()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var host = CreateHost();

        var applied = (await host.Services.ApplyMigrationSetsAsync("test", ["identity"])).ValueOrThrow();

        applied.Should().ContainSingle().Which.Set.Should().Be("identity");
        (await host.HasTableAsync("public", "orders")).Should().BeFalse();
        (await host.HasTableAsync("public", "identity_users")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyMigrationSets_ShouldFail_WhenAFilterNamesAnUnknownSet()
    {
        await using var host = CreateHost();

        var result = await host.Services.ApplyMigrationSetsAsync("test", ["billing"]);

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Message.Should().Contain("billing");
    }

    [Fact]
    public async Task Rollback_ShouldTouchOnlyItsOwnSet()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");

        await using var host = CreateHost(configureIdentity: options => options.AllowRollback = true);
        (await host.Services.ApplyMigrationSetsAsync("test")).ValueOrThrow();

        (await host.Sets.GetRunner("identity").RollbackAsync()).ValueOrThrow().Should().Equal("001-users");

        (await host.HasTableAsync("public", "identity_users")).Should().BeFalse();
        (await host.HasTableAsync("public", "orders")).Should().BeTrue();
        (await host.ReadHistoryAsync("migration_history")).Should().Equal("001-orders");
    }

    [Fact]
    public async Task Registry_ShouldThrow_WhenTwoSetsShareOneHistoryTable()
    {
        await using var host = CreateHost(configureIdentity: options => options.TableName = "migration_history");

        var resolve = () => host.Sets;

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*migration_history*");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Identity")]
    [InlineData("1identity")]
    [InlineData("identity-module")]
    public void AddDatabaseBespokeMigrationSet_ShouldRejectAReservedOrMalformedName(string name)
    {
        var services = new ServiceCollection();

        var register = () => services.AddDatabaseBespokeMigrationSet(name, _identity.Root);

        register.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddDatabaseBespokeMigrationSet_ShouldThrow_WhenTheSameSetIsRegisteredTwice()
    {
        var services = new ServiceCollection();
        services.AddDatabaseBespokeMigrationSet("identity", _identity.Root);

        var register = () => services.AddDatabaseBespokeMigrationSet("identity", _identity.Root);

        register.Should().Throw<InvalidOperationException>().WithMessage("*identity*");
    }

    [Fact]
    public async Task Registry_ShouldListOnlyTheDefaultSet_WhenTheHostRegistersOneSet()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");

        await using var host = MigrationSetHost.Sqlite(ConnectionString, services =>
            services.AddDatabaseBespokeMigrations(Workspace.Root, options => options.Provider = DatabaseProvider.Sqlite));

        host.Sets.Names.Should().Equal(MigrationConstants.DefaultSetName);
        host.Sets.GetRunner(MigrationConstants.DefaultSetName).Should().BeSameAs(host.Services.GetRequiredService<IMigrationRunnerService>());
        (await host.Services.ApplyMigrationSetsAsync("test")).ValueOrThrow()
            .Should().ContainSingle().Which.Applied.Should().Equal("001-orders");
        (await host.ReadHistoryAsync("migration_history")).Should().Equal("001-orders");
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();

    private MigrationSetHost CreateHost(Action<MigrationOptions>? configureIdentity = null) =>
        MigrationSetHost.Sqlite(ConnectionString, services =>
        {
            services.AddDatabaseBespokeMigrations(Workspace.Root, options => options.Provider = DatabaseProvider.Sqlite);
            services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
            {
                options.Provider = DatabaseProvider.Sqlite;
                configureIdentity?.Invoke(options);
            });
        });
}
