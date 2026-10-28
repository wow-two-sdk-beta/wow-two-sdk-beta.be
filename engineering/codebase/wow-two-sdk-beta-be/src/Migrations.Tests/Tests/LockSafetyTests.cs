using System.Diagnostics;
using AwesomeAssertions;
using Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>Waiting is bounded: a held advisory lock or table lock ends in busy, and the reserved key class keeps app locks apart.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class LockSafetyTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture)
{
    [Fact]
    public async Task Apply_ShouldReportBusyWithinTheBound_WhenAnotherSessionHoldsTheReservedKey()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using var host = CreateDefaultHost(configure: options => options.AdvisoryLockWait = TimeSpan.FromSeconds(1));
        var key = MigrationLockKey.Reserved(host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId);

        await using var holder = await OpenAsync("lock-holder");
        await holder.ExecuteAsync("select pg_advisory_lock(@keyClass, @key)", new { keyClass = key.KeyClass, key = (int)key.Key });

        var stopwatch = Stopwatch.StartNew();
        var result = await Engine(host).ApplyAsync();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        result.Exit.Should().Be(3);
        result.Sets.Single().Status.Should().Be(MigrateOutcome.Busy);
        result.Sets.Single().LockWaitMs.Should().BeGreaterThanOrEqualTo(900);
        (await TableExistsAsync("public", "items")).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_ShouldWaitForAnOlderApplicant_OnTheLegacyKey()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using var host = CreateDefaultHost(configure: options => options.AdvisoryLockWait = TimeSpan.FromSeconds(1));

        await using var olderApplicant = await OpenAsync();
        await olderApplicant.ExecuteAsync("select pg_advisory_lock(@id)", new { id = host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId });

        (await Engine(host).ApplyAsync()).Exit.Should().Be(3);
    }

    [Fact]
    public async Task Apply_ShouldNotWait_WhenAnAppHoldsATransactionLockOnTheSameNumbers()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using var host = CreateDefaultHost(configure: options => options.AdvisoryLockWait = TimeSpan.FromSeconds(1));
        var reserved = MigrationLockKey.Reserved(host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId);

        // The app's single-key lock on the very bits of the reserved pair lives in the other key space.
        await using var app = await OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        await app.ExecuteAsync("select pg_advisory_xact_lock(@bits)", new { bits = SameBits(reserved) }, tx);

        var result = await Engine(host).ApplyAsync();

        result.Exit.Should().Be(0);
        result.Sets.Single().Applied.Select(label => label.Label).Should().Equal("001-items");
    }

    [Fact]
    public async Task Apply_ShouldNotWait_WhenAnAppHoldsATransactionLockOnTheLockId_AndTheLegacyKeyIsOff()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using var host = CreateDefaultHost(configure: options =>
        {
            options.AdvisoryLockWait = TimeSpan.FromSeconds(1);
            options.TakeLegacyAdvisoryLock = false;
        });

        await using var app = await OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        await app.ExecuteAsync("select pg_advisory_xact_lock(@id)", new { id = host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId }, tx);

        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);
    }

    [Fact]
    public async Task HeldReservedKey_ShouldNeverBlockAnAppLock_OnTheSameNumbers()
    {
        var reserved = MigrationLockKey.Reserved(4_855_178_001L);
        var dialect = new PostgresMigrationDialect();

        await using var migrator = await OpenAsync();
        (await dialect.TryAcquireLockAsync(migrator, reserved)).Should().BeTrue();

        await using var app = await OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        (await app.ExecuteScalarAsync<bool>("select pg_try_advisory_xact_lock(@bits)", new { bits = SameBits(reserved) }, tx)).Should().BeTrue();
        (await app.ExecuteScalarAsync<bool>("select pg_try_advisory_xact_lock(@key)", new { key = reserved.Key }, tx)).Should().BeTrue();
    }

    [Fact]
    public async Task Apply_ShouldReportBusy_WhenATableLockOutlastsEveryAttempt()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using (var first = CreateDefaultHost())
            (await Engine(first).ApplyAsync()).Exit.Should().Be(0);

        Workspace.Write("002-note", "alter table items add column note text;", "alter table items drop column note;");
        await using var host = CreateDefaultHost(configure: options =>
        {
            options.LockTimeout = TimeSpan.FromMilliseconds(200);
            options.LockAttempts = 2;
            options.LockRetryDelay = TimeSpan.FromMilliseconds(50);
        });

        await using var reader = await OpenAsync();
        await using var tx = await reader.BeginTransactionAsync();
        await reader.ExecuteAsync("lock table items in access exclusive mode", transaction: tx);

        var result = await Engine(host).ApplyAsync();

        result.Exit.Should().Be(3);
        result.Sets.Single().LevelAfter.Should().Be(1);
        await tx.RollbackAsync();
        (await ScalarAsync<long>("select count(*) from information_schema.columns where table_name = 'items' and column_name = 'note'")).Should().Be(0);
    }

    [Fact]
    public async Task Apply_ShouldRetryTheBatch_WhenTheTableLockIsReleasedBetweenAttempts()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using (var first = CreateDefaultHost())
            (await Engine(first).ApplyAsync()).Exit.Should().Be(0);

        Workspace.Write("002-note", "-- @lock-timeout 1\nalter table items add column note text;", "alter table items drop column note;");
        await using var host = CreateDefaultHost(configure: options =>
        {
            options.LockAttempts = 5;
            options.LockRetryDelay = TimeSpan.FromMilliseconds(100);
        });

        await using var reader = await OpenAsync();
        var tx = await reader.BeginTransactionAsync();
        await reader.ExecuteAsync("lock table items in access exclusive mode", transaction: tx);
        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            await tx.RollbackAsync();
            await tx.DisposeAsync();
        });

        var result = await Engine(host).ApplyAsync();
        await release;

        result.Exit.Should().Be(0);
        result.Sets.Single().Applied.Select(label => label.Label).Should().Equal("002-note");
    }

    /// <summary>The single bigint whose high and low words equal a reserved pair's class and key.</summary>
    private static long SameBits(MigrationLockKey reserved) => ((long)reserved.KeyClass!.Value << 32) | (uint)(int)reserved.Key;
}
