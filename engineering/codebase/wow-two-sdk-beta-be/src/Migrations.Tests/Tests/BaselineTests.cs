using AwesomeAssertions;
using Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>A baseline records source migrations as applied without running them, so an existing database adopts a set.</summary>
public sealed class BaselineTests : SqliteMigratorTestBase
{
    [Fact]
    public async Task Baseline_ShouldRecordEveryMigrationWithoutRunningIt()
    {
        WriteUsersAndOrders();
        await using var migrator = CreateMigrator();
        await ExecuteAsync(migrator, "create table users(id integer primary key); create table orders(id integer primary key);");

        var baselined = (await migrator.Runner.BaselineAsync()).ValueOrThrow();

        baselined.Should().Equal("001-users", "002-orders");
        (await migrator.ReadHistoryAsync()).Should().AllSatisfy(row => row.AppliedBy.Should().Be(MigrationConstants.BaselineAppliedBy));
        (await migrator.Runner.ApplyPendingAsync("deploy")).ValueOrThrow().Should().BeEmpty();
        (await migrator.Runner.CheckLevelAsync()).ValueOrThrow().IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Baseline_ShouldLeaveLaterMigrationsPending_WhenAnOrdinalIsGiven()
    {
        WriteUsersAndOrders();
        await using var migrator = CreateMigrator();
        await ExecuteAsync(migrator, "create table users(id integer primary key);");

        (await migrator.Runner.BaselineAsync(1)).ValueOrThrow().Should().Equal("001-users");

        (await migrator.Runner.ApplyPendingAsync("deploy")).ValueOrThrow().Should().Equal("002-orders");
        (await migrator.HasTableAsync("orders")).Should().BeTrue();
    }

    [Fact]
    public async Task Baseline_ShouldChangeNothing_WhenEveryMigrationIsAlreadyRecorded()
    {
        WriteUsersAndOrders();
        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("deploy")).ValueOrThrow();

        (await migrator.Runner.BaselineAsync()).ValueOrThrow().Should().BeEmpty();

        (await migrator.ReadHistoryAsync()).Should().AllSatisfy(row => row.AppliedBy.Should().Be("deploy"));
    }

    [Fact]
    public async Task Baseline_ShouldRefuse_WhenTheSetRecordsOtherHistory()
    {
        WriteUsersAndOrders();
        await using var migrator = CreateMigrator();
        (await migrator.Runner.BaselineAsync(1)).ValueOrThrow();

        var result = await migrator.Runner.BaselineAsync();

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        (await migrator.ReadHistoryAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Baseline_ShouldFail_WhenTheOrdinalIsNotInTheSource()
    {
        WriteUsersAndOrders();
        await using var migrator = CreateMigrator();

        var result = await migrator.Runner.BaselineAsync(7);

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Validation);
        error.Message.Should().Contain("007");
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
    }

    [Fact]
    public async Task Baseline_ShouldClaimTheSchema_WhenTheSetOwnsIt()
    {
        WriteUsersAndOrders();
        await using var migrator = CreateOwned("identity-haven@prod");

        (await migrator.Runner.BaselineAsync()).ValueOrThrow();

        var owner = (await migrator.Runner.GetStatusAsync()).ValueOrThrow().SchemaOwner!;
        owner.Instance.Should().Be("identity-haven@prod");
        owner.ClaimedBy.Should().Be(MigrationConstants.BaselineAppliedBy);
    }

    [Fact]
    public async Task Baseline_ShouldRefuse_WhenAnotherInstanceOwnsTheSchema()
    {
        WriteUsersAndOrders();
        await using (var haven = CreateOwned("identity-haven@prod"))
            (await haven.Runner.BaselineAsync(1)).ValueOrThrow();

        await using var other = CreateOwned("identity-forever-pin@prod");
        var result = await other.Runner.BaselineAsync();

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Conflict);
        error.Message.Should().Contain("identity-haven@prod");
    }

    private void WriteUsersAndOrders()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        Workspace.Write("002-orders", "create table orders(id integer primary key);", "drop table orders;");
    }

    private MigratorHarness CreateOwned(string instance) =>
        CreateMigrator(options =>
        {
            options.SchemaName = "identity";
            options.SchemaOwner = "service";
            options.OwnerInstance = instance;
        });

    /// <summary>Builds the tables an existing database already holds, outside the migrator.</summary>
    private static async Task ExecuteAsync(MigratorHarness migrator, string sql)
    {
        await using var connection = await migrator.OpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }
}
