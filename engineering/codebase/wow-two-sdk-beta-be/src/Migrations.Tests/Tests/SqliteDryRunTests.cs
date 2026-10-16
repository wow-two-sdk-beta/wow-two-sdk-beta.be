using AwesomeAssertions;
using Dapper;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>Verifies the dry run on SQLite, whose transactional DDL lets one rolled-back transaction rehearse the batch.</summary>
public sealed class SqliteDryRunTests : SqliteMigratorTestBase
{
    [Fact]
    public async Task DryRun_ShouldRehearseAndRollBack_WhenEveryMigrationSucceeds()
    {
        Workspace.Write("001-items", "create table t1(id int primary key); insert into t1(id) values (1);", "drop table t1;");
        Workspace.Write("002-bare-index", "-- @no-transaction\ncreate index if not exists ix_t1_id on t1(id);", "drop index if exists ix_t1_id;");
        Workspace.Write("003-tags", "create table t2(id int primary key);", "drop table t2;");
        await using var migrator = CreateMigrator();

        var report = (await migrator.Runner.DryRunPendingAsync(CancellationToken.None)).ValueOrThrow();

        report.Passed.Should().BeTrue();
        report.Succeeded.Should().Equal("001-items", "003-tags");
        report.NotRehearsed.Should().Equal("002-bare-index");
        (await migrator.HasTableAsync("t1")).Should().BeFalse();
        (await migrator.HasTableAsync("t2")).Should().BeFalse();
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_ShouldReportTheFailure_AndKeepAppliedHistory()
    {
        Workspace.Write("001-items", "create table t1(id int primary key);", "drop table t1;");
        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        Workspace.Write("002-seed", "insert into t1(id) values (1);", "delete from t1;");
        Workspace.Write("003-broken", "create table t3(this is not valid sql);", "drop table t3;");

        var report = (await migrator.Runner.DryRunPendingAsync(CancellationToken.None)).ValueOrThrow();

        report.Passed.Should().BeFalse();
        report.Succeeded.Should().Equal("002-seed");
        report.Failure!.Migration.Should().Be("003-broken");
        (await migrator.ReadHistoryAsync()).Select(row => row.Ordinal).Should().Equal(1);
        await using var connection = await migrator.OpenConnectionAsync();
        (await connection.ExecuteScalarAsync<long>("select count(*) from t1")).Should().Be(0);
    }
}
