using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Backend.Beta.Data.Dapper;
using WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Harness;
using WoW.Two.Sdk.Backend.Beta.Foundation.Results;

namespace WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Tests;

/// <summary>
/// Failure mid-batch on SQLite, which has transactional DDL: by default the pending transactional migrations commit
/// as one batch, so an invalid #2 rolls back #1 as well and leaves no history row. The per-migration opt-out keeps
/// the older behaviour, where #1 stays applied. Fixing #2 and re-running completes the batch either way.
/// </summary>
public sealed class FailureMidBatchTests : SqliteMigratorTestBase
{
    [Fact]
    public async Task ApplyPending_ShouldRollBackTheWholeBatch_WhenMiddleMigrationInvalid()
    {
        WriteBatchWithBrokenMiddle();
        await using var migrator = CreateMigrator();

        await Assert.ThrowsAsync<SqliteException>(
            () => migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        // The batch rolled back: no table and no history row from any of the three.
        (await migrator.HasTableAsync("t1")).Should().BeFalse();
        (await migrator.HasTableAsync("t2")).Should().BeFalse();
        (await migrator.HasTableAsync("t3")).Should().BeFalse();
        (await migrator.ReadHistoryAsync()).Should().BeEmpty();

        var status = (await migrator.Runner.GetStatusAsync(CancellationToken.None)).ValueOrThrow();
        status.Pending.Select(p => p.Ordinal).Should().Equal(1, 2, 3);
        status.Drifted.Should().BeEmpty();

        // Fix #2 and re-run: the whole batch applies.
        Workspace.OverwriteApply("002-broken", "create table t2(id int primary key);");
        var applied = (await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        applied.Should().Equal("001-first", "002-broken", "003-third");
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ApplyPending_ShouldKeepEarlierMigrations_WhenPerMigrationTransactionsAreConfigured()
    {
        WriteBatchWithBrokenMiddle();
        await using var migrator = CreateMigrator(options => options.TransactionScope = MigrationTransactionScope.PerMigration);

        await Assert.ThrowsAsync<SqliteException>(
            () => migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        // #1 committed on its own; #2 rolled back its own transaction; #3 never ran.
        (await migrator.HasTableAsync("t1")).Should().BeTrue();
        (await migrator.HasTableAsync("t2")).Should().BeFalse();
        (await migrator.HasTableAsync("t3")).Should().BeFalse();
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1);

        Workspace.OverwriteApply("002-broken", "create table t2(id int primary key);");
        (await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None))
            .ValueOrThrow().Should().Equal("002-broken", "003-third");
    }

    [Fact]
    public async Task ApplyPending_ShouldLogTheFailedAndRolledBackMigrations_WhenTheBatchFails()
    {
        WriteBatchWithBrokenMiddle();
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(logs))
            .AddSqliteConnectionFactory(ConnectionString)
            .AddDatabaseBespokeMigrations(Workspace.Root, options => options.Provider = DatabaseProvider.Sqlite);
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<SqliteException>(
            () => provider.GetRequiredService<IMigrationRunnerService>().ApplyPendingAsync("test", CancellationToken.None));

        var failure = logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Which;
        failure.Message.Should().Be(
            "Migration 002-broken failed; rolled back 001-first, 002-broken, so none of them is recorded. " +
            "Committed earlier in this run: none. Not attempted: 003-third.");
        failure.Exception.Should().BeOfType<SqliteException>();
    }

    private void WriteBatchWithBrokenMiddle()
    {
        Workspace.Write("001-first",
            applySql: "create table t1(id int primary key);",
            rollbackSql: "drop table t1;");
        Workspace.Write("002-broken",
            applySql: "create table t2(this is not valid sql);", // intentional syntax error
            rollbackSql: "drop table t2;");
        Workspace.Write("003-third",
            applySql: "create table t3(id int primary key);",
            rollbackSql: "drop table t3;");
    }
}
