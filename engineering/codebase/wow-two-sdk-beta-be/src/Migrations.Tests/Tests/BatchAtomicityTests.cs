using AwesomeAssertions;
using Npgsql;
using WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Backend.Beta.Foundation.Results;
using WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Harness;
using WoW.Two.Sdk.Backend.Beta.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Tests;

/// <summary>Verifies on PostgreSQL that a failed apply never leaves part of a transactional batch behind.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class BatchAtomicityTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    [Fact]
    public async Task ApplyPending_ShouldRollBackTheWholeBatch_WhenTheThirdMigrationFails()
    {
        Workspace.Write("001-items", """
            create table batch_item(id integer primary key);
            insert into batch_item(id) values (1);
            """, "drop table batch_item;");
        Workspace.Write("002-tags", "create table batch_tag(id integer primary key);", "drop table batch_tag;");
        Workspace.Write("003-broken", "alter table batch_missing add column name text;", "-- nothing to undo");

        await using var migrator = CreateMigrator();
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None));

        failure.SqlState.Should().Be(PostgresErrorCodes.UndefinedTable);
        (await migrator.HasTableAsync("batch_item")).Should().BeFalse();
        (await migrator.HasTableAsync("batch_tag")).Should().BeFalse();
        (await migrator.ReadHistoryAsync()).Should().BeEmpty();

        Workspace.OverwriteApply("003-broken", "alter table batch_item add column name text;");
        (await migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None))
            .ValueOrThrow().Should().Equal("001-items", "002-tags", "003-broken");
        (await migrator.ReadHistoryAsync()).Select(row => row.Ordinal).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ApplyPending_ShouldCommitTheBatchBeforeANoTransactionScript_AndRollBackOnlyTheBatchAfterIt()
    {
        Workspace.Write("001-items", "create table split_item(id integer primary key, name text);", "drop table split_item;");
        Workspace.Write("002-tags", "create table split_tag(id integer primary key);", "drop table split_tag;");
        Workspace.Write("003-name-index", """
            -- @no-transaction
            create index concurrently if not exists ix_split_item_name on split_item(name);
            """, "drop index concurrently if exists ix_split_item_name;");
        Workspace.Write("004-notes", "create table split_note(id integer primary key);", "drop table split_note;");
        Workspace.Write("005-broken", "alter table split_missing add column body text;", "-- nothing to undo");

        await using var migrator = CreateMigrator();
        await Assert.ThrowsAsync<PostgresException>(
            () => migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None));

        // The batch before the bare script committed, the bare script committed, the batch after it rolled back.
        (await migrator.ReadHistoryAsync()).Select(row => row.Ordinal).Should().Equal(1, 2, 3);
        (await migrator.HasTableAsync("split_item")).Should().BeTrue();
        (await migrator.HasTableAsync("split_tag")).Should().BeTrue();
        (await migrator.HasIndexAsync("ix_split_item_name")).Should().BeTrue();
        (await migrator.HasTableAsync("split_note")).Should().BeFalse();

        var status = (await migrator.Runner.GetStatusAsync(CancellationToken.None)).ValueOrThrow();
        status.Pending.Select(migration => migration.Label).Should().Equal("004-notes", "005-broken");
    }

    [Fact]
    public async Task ApplyPending_ShouldKeepEarlierMigrations_WhenPerMigrationTransactionsAreConfigured()
    {
        Workspace.Write("001-items", "create table single_item(id integer primary key);", "drop table single_item;");
        Workspace.Write("002-tags", "create table single_tag(id integer primary key);", "drop table single_tag;");
        Workspace.Write("003-broken", "alter table single_missing add column name text;", "-- nothing to undo");

        await using var migrator = CreateMigrator(options => options.TransactionScope = MigrationTransactionScope.PerMigration);
        await Assert.ThrowsAsync<PostgresException>(
            () => migrator.Runner.ApplyPendingAsync("deploy", CancellationToken.None));

        (await migrator.ReadHistoryAsync()).Select(row => row.Ordinal).Should().Equal(1, 2);
        (await migrator.HasTableAsync("single_item")).Should().BeTrue();
        (await migrator.HasTableAsync("single_tag")).Should().BeTrue();
    }
}
