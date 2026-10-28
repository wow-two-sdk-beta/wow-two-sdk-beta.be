using AwesomeAssertions;
using Dapper;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The operator verbs write what they say, record one event each, and change nothing on a rerun.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class OperatorVerbTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture)
{
    [Fact]
    public async Task Forget_ShouldDeleteTheRowRunNoSqlAndRecordOneEvent()
    {
        await ApplyThroughAsync("create table t33(id integer primary key);");
        Workspace.DeleteFolder("033-newer");
        await using var older = CreateDefaultHost();

        var forgot = await Engine(older).ForgetAsync(null, 32);

        forgot.Exit.Should().Be(0);
        forgot.Sets.Single().Removed.Should().Equal(new MigrationLabel("033-newer", MigrationKind.Expand));
        forgot.Sets.Single().LevelBefore.Should().Be(33);
        forgot.Sets.Single().LevelAfter.Should().Be(32);
        (await TableExistsAsync("public", "t33")).Should().BeTrue("forget runs no SQL");
        (await ReadEventsAsync()).Should().Equal(("forget", "033"));

        var rerun = await Engine(older).ForgetAsync(null, 32);
        rerun.Exit.Should().Be(0);
        rerun.Sets.Single().Status.Should().Be(MigrateOutcome.Current);
        (await ReadEventsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Forget_ShouldRefuseAContract_UnlessForced()
    {
        await ApplyThroughAsync("-- @contract since=v0.6\ndrop table t32;");
        Workspace.DeleteFolder("033-newer");
        await using var older = CreateDefaultHost();

        var refused = await Engine(older).ForgetAsync(null, 32);
        refused.Exit.Should().Be(1);
        (await ScalarAsync<long>("select count(*) from migration_history where ordinal = 33")).Should().Be(1);

        var forced = await Engine(older).ForgetAsync(null, 32, force: true);
        forced.Exit.Should().Be(0);
        forced.Sets.Single().Removed.Should().Equal(new MigrationLabel("033-newer", MigrationKind.Contract));
    }

    [Fact]
    public async Task Forget_ShouldRefuseAKindlessRow_UnlessForced()
    {
        await ApplyThroughAsync("create table t33(id integer primary key);");
        await ExecuteAsync("update migration_history set kind = null where ordinal = 33");
        Workspace.DeleteFolder("033-newer");
        await using var older = CreateDefaultHost();

        (await Engine(older).ForgetAsync(null, 32)).Exit.Should().Be(1);
        (await Engine(older).ForgetAsync(null, 32, force: true)).Exit.Should().Be(0);
    }

    [Fact]
    public async Task Rollback_ShouldRunTheScriptsRecordOneEvent_AndChangeNothingAtTheTarget()
    {
        await ApplyThroughAsync("create table t33(id integer primary key);");
        await using var host = CreateDefaultHost();

        var atTarget = await Engine(host).RollbackAsync(null, 33);
        atTarget.Exit.Should().Be(0);
        atTarget.Sets.Single().Status.Should().Be(MigrateOutcome.Current);

        var rolledBack = await Engine(host).RollbackAsync(null, 32);
        rolledBack.Sets.Single().Status.Should().Be(MigrateOutcome.Done);
        rolledBack.Sets.Single().Removed.Select(label => label.Label).Should().Equal("033-newer");
        (await TableExistsAsync("public", "t33")).Should().BeFalse();
        (await ReadEventsAsync()).Should().Equal(("rollback", "033"));

        (await Engine(host).RollbackAsync(null, 32)).Sets.Single().Status.Should().Be(MigrateOutcome.Current);
        (await ReadEventsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RepairAndBaseline_ShouldRecordOneEventEach_AndRerunAsCurrent()
    {
        Workspace.Write("001-users", "create table if not exists users(id integer primary key);", "drop table users;");
        await using var host = CreateDefaultHost();

        var baseline = await Engine(host).BaselineAsync(null, 1);
        baseline.Sets.Single().Applied.Should().Equal(new MigrationLabel("001-users", MigrationKind.Expand));
        (await Engine(host).BaselineAsync(null, 1)).Sets.Single().Status.Should().Be(MigrateOutcome.Current);

        Workspace.OverwriteApply("001-users", "create table if not exists users(id integer primary key); -- reworded");
        var repair = await Engine(host).RepairAsync(null);
        repair.Sets.Single().Repaired.Select(label => label.Label).Should().Equal("001-users");
        (await Engine(host).RepairAsync(null)).Sets.Single().Status.Should().Be(MigrateOutcome.Current);

        (await ReadEventsAsync()).Should().Equal(("baseline", "001"), ("repair", "001"));
    }

    [Fact]
    public async Task Claim_ShouldReplaceTheMarkerRecordOneEvent_AndRerunAsCurrent()
    {
        Workspace.Write("001-users", "create table {{schema}}.users(id integer primary key);", "drop table {{schema}}.users;");
        await using var module = CreateHost(services => services.AddDatabaseBespokeMigrationSet("accounts", Workspace.Root, options =>
        {
            options.SchemaName = "accounts";
            options.SchemaOwner = "module";
            options.OwnerInstance = "shop@prod";
        }));
        (await Engine(module).ApplyAsync()).Exit.Should().Be(0);

        var claim = await Engine(module).ClaimAsync("accounts", "service", "identity-shop@prod");

        claim.Exit.Should().Be(0);
        claim.Sets.Single().Message.Should().Contain("from set 'accounts' as 'module' of 'shop@prod'");
        (await ScalarAsync<string>("select owner || ' ' || instance from accounts.migration_schema_owner")).Should().Be("service identity-shop@prod");
        (await ReadEventsAsync("accounts.accounts_migration_history_events")).Should().Equal(("claim", ""));
        (await Engine(module).ClaimAsync("accounts", "service", "identity-shop@prod")).Sets.Single().Status.Should().Be(MigrateOutcome.Current);
        (await Engine(module).CheckAsync()).Sets.Single().State.Should().Be(MigrationLevelState.OwnerConflict);
    }

    [Fact]
    public async Task LocksAndUnlock_ShouldShowTheHolder_ThenEndItsSession()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        await using var host = CreateDefaultHost();
        var key = MigrationLockKey.Reserved(host.Sets.GetOptions(MigrationConstants.DefaultSetName).AdvisoryLockId);
        await using var holder = await OpenAsync("hung-migrator");
        await holder.ExecuteAsync("select pg_advisory_lock(@keyClass, @key)", new { keyClass = key.KeyClass, key = (int)key.Key });

        var locks = await Engine(host).LocksAsync();
        locks.Exit.Should().Be(0);
        var held = locks.Sets.Single().Locks.Should().ContainSingle().Which;
        held.ApplicationName.Should().Be("hung-migrator");
        held.Key.Should().Be("reserved");
        held.Pid.Should().Be(holder.ProcessID);

        var unlock = await Engine(host).UnlockAsync(null);
        unlock.Sets.Single().Status.Should().Be(MigrateOutcome.Done);
        unlock.Sets.Single().Message.Should().Contain($"pid {holder.ProcessID}");
        var probe = () => holder.ExecuteScalarAsync<int>("select 1");
        await probe.Should().ThrowAsync<NpgsqlException>();
        (await ReadEventsAsync()).Should().Equal(("unlock", ""));

        (await Engine(host).UnlockAsync(null)).Sets.Single().Status.Should().Be(MigrateOutcome.Current);
        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);
    }

    [Fact]
    public async Task OneSetVerbs_ShouldRefuse_WhenTheHostHasSeveralSetsAndNoneIsNamed()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        using var other = new MigrationsWorkspace();
        await using var host = CreateHost(services =>
        {
            services.AddDatabaseBespokeMigrations(Workspace.Root);
            services.AddDatabaseBespokeMigrationSet("billing", other.Root);
        });

        var result = await Engine(host).ForgetAsync(null, 0);

        result.Exit.Should().Be(1);
        result.Message.Should().Contain("Name one set");
    }

    private async Task ApplyThroughAsync(string newerSql)
    {
        Workspace.Write("032-older", "create table t32(id integer primary key);", "drop table t32;");
        Workspace.Write("033-newer", newerSql, "drop table if exists t33;");
        await using var newer = CreateDefaultHost("0.6.0");
        (await Engine(newer).ApplyAsync()).Exit.Should().Be(0);
    }
}
