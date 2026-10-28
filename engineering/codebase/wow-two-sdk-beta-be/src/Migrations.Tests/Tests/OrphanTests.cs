using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>
/// Orphan: an applied history row whose source folder is gone. Apply fails closed with a <c>DataIntegrity</c> failure on an
/// orphan below the code's level unless <c>AllowOrphanedHistory</c> lets it proceed; an orphan above the level is an ahead,
/// tolerated when this release survives it.
/// </summary>
public sealed class OrphanTests : SqliteMigratorTestBase
{
    private static readonly int[] OrphanedOrdinals = [2];

    [Fact]
    public async Task ApplyPending_ShouldRefuseAnOrphan_WhenItSitsBelowTheLevel()
    {
        WriteThree();
        await using var migrator = CreateMigrator();
        (await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        // Delete the source of an applied migration below the level → its history row is now orphaned.
        Workspace.DeleteFolder("002-second");

        var status = (await migrator.Runner.GetStatusAsync(CancellationToken.None)).ValueOrThrow();
        status.Orphaned.Should().Contain(2);

        var failure = await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None);
        failure.IsFailure(out var orphanError, out _).Should().BeTrue();
        orphanError!.Type.Should().Be(AppErrorType.DataIntegrity);
        orphanError.Metadata!["orphaned"].Should().BeEquivalentTo(OrphanedOrdinals);
    }

    [Fact]
    public async Task ApplyPending_ShouldProceed_WhenTheOrphanAboveTheLevelIsAnExpand()
    {
        WriteThree();
        await using (var newer = CreateMigrator())
            (await newer.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        Workspace.DeleteFolder("003-third");
        await using var older = CreateMigrator();

        (await older.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow().Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPending_ShouldProceed_WhenOrphanAllowed()
    {
        WriteThree();
        await using (var seed = CreateMigrator())
            (await seed.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        Workspace.DeleteFolder("002-second");

        // A later migration plus a migrator that tolerates orphaned history applies the pending work without throwing.
        Workspace.Write("004-fourth",
            applySql: "create table t4(id int primary key);",
            rollbackSql: "drop table t4;");

        await using var migrator = CreateMigrator(o => o.AllowOrphanedHistory = true);
        var applied = (await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        applied.Should().Contain("004-fourth");
        (await migrator.HasTableAsync("t4")).Should().BeTrue();
        // Orphan still reported in status, but it no longer blocks apply.
        (await migrator.Runner.GetStatusAsync(CancellationToken.None)).ValueOrThrow().Orphaned.Should().Contain(2);
    }

    private void WriteThree()
    {
        Workspace.Write("001-baseline", applySql: "create table t1(id int primary key);", rollbackSql: "drop table t1;");
        Workspace.Write("002-second", applySql: "create table t2(id int primary key);", rollbackSql: "drop table t2;");
        Workspace.Write("003-third", applySql: "create table t3(id int primary key);", rollbackSql: "drop table t3;");
    }
}
