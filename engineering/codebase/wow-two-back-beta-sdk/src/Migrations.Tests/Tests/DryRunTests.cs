using AwesomeAssertions;
using Dapper;
using Npgsql;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Foundation.Results;
using WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Harness;
using WoW.Two.Sdk.Backend.Beta.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Tests;

/// <summary>Verifies on PostgreSQL that a dry run rehearses pending migrations, reports the outcome, and never commits.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class DryRunTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    [Fact]
    public async Task DryRun_ShouldLeaveSchemaAndHistoryUntouched_WhenEveryMigrationSucceeds()
    {
        Workspace.Write("001-items", "create table dry_item(id integer primary key);", "drop table dry_item;");
        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None)).ValueOrThrow();

        Workspace.Write("002-seed", "insert into dry_item(id) values (1);", "delete from dry_item;");
        Workspace.Write("003-tags", "create table dry_tag(id integer primary key);", "drop table dry_tag;");

        var report = (await migrator.Runner.DryRunPendingAsync(CancellationToken.None)).ValueOrThrow();

        report.Passed.Should().BeTrue();
        report.WouldApply.Should().Equal("002-seed", "003-tags");
        report.Succeeded.Should().Equal("002-seed", "003-tags");
        report.NotRehearsed.Should().BeEmpty();
        (await migrator.ReadHistoryAsync()).Select(row => row.Ordinal).Should().Equal(1);
        (await migrator.HasTableAsync("dry_tag")).Should().BeFalse();
        await using (var connection = await migrator.OpenConnectionAsync())
            (await connection.ExecuteScalarAsync<long>("select count(*) from dry_item")).Should().Be(0);

        (await migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None))
            .ValueOrThrow().Should().Equal("002-seed", "003-tags");
    }

    [Fact]
    public async Task DryRun_ShouldNotCreateTheHistoryTable_OnAFreshDatabase()
    {
        Workspace.Write("001-items", "create table fresh_item(id integer primary key);", "drop table fresh_item;");
        await using var migrator = CreateMigrator();

        var report = (await migrator.Runner.DryRunPendingAsync(CancellationToken.None)).ValueOrThrow();

        report.Succeeded.Should().Equal("001-items");
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
        (await migrator.HasTableAsync("fresh_item")).Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_ShouldReportTheFirstFailure_AndLeaveTheSchemaUntouched()
    {
        Workspace.Write("001-items", "create table failing_item(id integer primary key);", "drop table failing_item;");
        Workspace.Write("002-broken", "alter table failing_missing add column name text;", "-- nothing to undo");
        Workspace.Write("003-tags", "create table failing_tag(id integer primary key);", "drop table failing_tag;");
        await using var migrator = CreateMigrator();

        var report = (await migrator.Runner.DryRunPendingAsync(CancellationToken.None)).ValueOrThrow();

        report.Passed.Should().BeFalse();
        report.WouldApply.Should().Equal("001-items", "002-broken", "003-tags");
        report.Succeeded.Should().Equal("001-items");
        report.Failure!.Migration.Should().Be("002-broken");
        report.Failure.SqlState.Should().Be(PostgresErrorCodes.UndefinedTable);
        report.Failure.Error.Should().Contain("failing_missing");
        report.Failure.FollowsNotRehearsed.Should().BeFalse();
        (await migrator.HasTableAsync("failing_item")).Should().BeFalse();
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_ShouldListNoTransactionScriptsAsNotRehearsed()
    {
        Workspace.Write("001-items", "create table bare_item(id integer primary key, name text);", "drop table bare_item;");
        Workspace.Write("002-name-index", """
            -- @no-transaction
            create index concurrently if not exists ix_bare_item_name on bare_item(name);
            """, "drop index concurrently if exists ix_bare_item_name;");
        Workspace.Write("003-tags", "create table bare_tag(id integer primary key);", "drop table bare_tag;");
        await using var migrator = CreateMigrator();

        var report = (await migrator.Runner.DryRunPendingAsync(CancellationToken.None)).ValueOrThrow();

        report.Passed.Should().BeTrue();
        report.Succeeded.Should().Equal("001-items", "003-tags");
        report.NotRehearsed.Should().Equal("002-name-index");
        (await migrator.HasIndexAsync("ix_bare_item_name")).Should().BeFalse();
        (await migrator.HasTableAsync("bare_item")).Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_ShouldFailLikeApply_WhenAnAppliedMigrationDrifted()
    {
        Workspace.Write("001-items", "create table drift_item(id integer primary key);", "drop table drift_item;");
        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None)).ValueOrThrow();
        Workspace.OverwriteApply("001-items", "create table drift_item(id bigint primary key);");

        var result = await migrator.Runner.DryRunPendingAsync(CancellationToken.None);

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.DataIntegrity);
    }

    [Fact]
    public async Task DryRun_ShouldWaitForTheAdvisoryLock_WhileAnotherApplicantHoldsIt()
    {
        Workspace.Write("001-items", "create table locked_item(id integer primary key);", "drop table locked_item;");
        await using var migrator = CreateMigrator();

        await using var holder = new NpgsqlConnection(Fixture.ConnectionString);
        await holder.OpenAsync();
        await holder.ExecuteAsync("select pg_advisory_lock(@id)", new { id = migrator.Options.AdvisoryLockId });

        var dryRun = migrator.Runner.DryRunPendingAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        dryRun.IsCompleted.Should().BeFalse("the dry run takes the same advisory lock as apply");

        await holder.ExecuteAsync("select pg_advisory_unlock(@id)", new { id = migrator.Options.AdvisoryLockId });
        (await dryRun).ValueOrThrow().Succeeded.Should().Equal("001-items");
    }
}
