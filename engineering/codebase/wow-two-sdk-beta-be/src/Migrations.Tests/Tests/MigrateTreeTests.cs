using System.Diagnostics;
using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The image's migrate tree: exits 0 to 3 per verb, the confirm guard, the result line, the bare migrate and the migrate key.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class MigrateTreeTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture), IDisposable
{
    private readonly string _seeds = Path.Combine(Path.GetTempPath(), "wow2-tree-seeds", Guid.NewGuid().ToString("N"));

    private string Database => new NpgsqlConnectionStringBuilder(Fixture.ConnectionString).Database!;

    [Fact]
    public async Task Check_ShouldReportBehindAndCreateNothing_OnAnEmptyDatabase()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        await using var host = CreateHost(services => services
            .AddDatabaseBespokeMigrations(Workspace.Root, options => options.LocalRun = false)
            .AddDatabaseBespokeMigrationSet("billing", Workspace.Root, options =>
            {
                options.SchemaName = "billing";
                options.SchemaOwner = "module";
            }));

        var check = await MigrateTree.RunAsync(host.Services, "check");

        check.Exit.Should().Be(1, check.ToString());
        check.Result.Sets.Select(set => set.State).Should().Equal(MigrationLevelState.Behind, MigrationLevelState.Behind);
        (await ScalarAsync<long>("select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname in ('public', 'billing')"))
            .Should().Be(0, "check writes no history, events table or marker");
        (await ScalarAsync<bool>("select exists (select 1 from pg_namespace where nspname = 'billing')")).Should().BeFalse();
    }

    [Fact]
    public async Task EveryReadAndWriteVerb_ShouldExitZero_OnACurrentDatabase()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        await using var host = CreateDefaultHost();
        (await MigrateTree.RunAsync(host.Services, "apply")).Exit.Should().Be(0);

        string[][] verbs =
        [
            ["check"], ["plan"], ["apply"], ["apply", "--dry-run"], ["seed"], ["sets"], ["locks"],
            ["rollback", "--to", "1", "--confirm", Database], ["forget", "--above", "1", "--confirm", Database],
            ["repair", "--confirm", Database], ["baseline", "--to", "1", "--confirm", Database], ["unlock", "--confirm", Database],
        ];
        foreach (var verb in verbs)
        {
            var run = await MigrateTree.RunAsync(host.Services, verb);
            run.Exit.Should().Be(0, $"{string.Join(' ', verb)}: {run}");
            run.Result.Sets.Should().OnlyContain(set => set.Status == MigrateOutcome.Current || set.Status == MigrateOutcome.Done);
        }

        (await TableExistsAsync("public", "migration_history_events")).Should().BeFalse("no verb changed anything, so none recorded an event");
    }

    [Fact]
    public async Task Drift_ShouldRefuseWithOne()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        await using var host = CreateDefaultHost();
        (await MigrateTree.RunAsync(host.Services, "apply")).Exit.Should().Be(0);
        Workspace.OverwriteApply("001-users", "create table users(id bigint primary key);");

        foreach (var verb in new[] { new[] { "check" }, ["apply"], ["apply", "--dry-run"] })
        {
            var run = await MigrateTree.RunAsync(host.Services, verb);
            run.Exit.Should().Be(1, $"{string.Join(' ', verb)}: {run}");
        }
    }

    [Fact]
    public async Task BrokenScript_ShouldFailWithTwo_AndRollTheBatchBack()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        Workspace.Write("002-broken", "alter table missing add column name text;", "select 1;");
        await using var host = CreateDefaultHost();

        var rehearsal = await MigrateTree.RunAsync(host.Services, "apply", "--dry-run");
        var apply = await MigrateTree.RunAsync(host.Services, "apply");

        rehearsal.Exit.Should().Be(2, rehearsal.ToString());
        apply.Exit.Should().Be(2, apply.ToString());
        apply.Set.SqlState.Should().Be("42P01");
        (await TableExistsAsync("public", "items")).Should().BeFalse();
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("seed")]
    [InlineData("rollback", "--to", "0")]
    [InlineData("forget", "--above", "0")]
    [InlineData("repair")]
    [InlineData("baseline", "--to", "1")]
    public async Task HeldLock_ShouldReportBusyWithinTheBound(params string[] verb)
    {
        await using var host = CreateSeededHost(options => options.AdvisoryLockWait = TimeSpan.FromSeconds(1));
        if (verb[0] is not ("apply" or "baseline"))
            (await MigrateTree.RunAsync(host.Services, "apply")).Exit.Should().Be(0);
        var key = MigrationLockKey.Reserved(host.Sets.GetOptions("catalog").AdvisoryLockId);
        await using var holder = await OpenAsync("lock-holder");
        await holder.ExecuteAsync("select pg_advisory_lock(@keyClass, @key)", new { keyClass = key.KeyClass, key = (int)key.Key });

        var stopwatch = Stopwatch.StartNew();
        var run = await MigrateTree.RunAsync(host.Services, [.. verb, .. verb[0] is "apply" or "seed" ? [] : new[] { "--confirm", Database }]);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        run.Exit.Should().Be(3, run.ToString());
        run.Set.Status.Should().Be(MigrateOutcome.Busy);
    }

    [Fact]
    public async Task Forget_ShouldDeleteRow33AndWriteOneEvent_OnlyWithTheDatabaseConfirmed()
    {
        Workspace.Write("032-older", "create table t32(id integer primary key);", "drop table t32;");
        Workspace.Write("033-newer", "create table t33(id integer primary key);", "drop table t33;");
        await using (var newer = CreateDefaultHost("0.6.0"))
            (await MigrateTree.RunAsync(newer.Services, "apply")).Exit.Should().Be(0);
        Workspace.DeleteFolder("033-newer");
        await using var older = CreateDefaultHost();

        var unconfirmed = await MigrateTree.RunAsync(older.Services, "forget", "--above", "32");
        var mistyped = await MigrateTree.RunAsync(older.Services, "forget", "--above", "32", "--confirm", "prod");

        unconfirmed.Exit.Should().Be(1);
        unconfirmed.Result.Message.Should().Contain($"--confirm {Database}");
        mistyped.Exit.Should().Be(1);
        (await ScalarAsync<long>("select count(*) from migration_history where ordinal = 33")).Should().Be(1);
        (await TableExistsAsync("public", "migration_history_events")).Should().BeFalse();

        var forgot = await MigrateTree.RunAsync(older.Services, "forget", "--above", "32", "--confirm", Database);

        forgot.Exit.Should().Be(0, forgot.ToString());
        forgot.Set.Removed.Select(label => label.Label).Should().Equal("033-newer");
        (await ScalarAsync<long>("select count(*) from migration_history where ordinal = 33")).Should().Be(0);
        (await TableExistsAsync("public", "t33")).Should().BeTrue("forget runs no SQL");
        (await ReadEventsAsync()).Should().Equal(("forget", "033"));

        var rerun = await MigrateTree.RunAsync(older.Services, "forget", "--above", "32", "--confirm", Database);
        rerun.Exit.Should().Be(0);
        rerun.Set.Status.Should().Be(MigrateOutcome.Current);
        (await ReadEventsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Forget_ShouldNeedIncludeContracts_ToForgetAContract()
    {
        Workspace.Write("032-older", "create table t32(id integer primary key);", "drop table t32;");
        Workspace.Write("033-newer", "-- @contract since=v0.6\ndrop table t32;", "select 1;");
        await using (var newer = CreateDefaultHost("0.6.0"))
            (await MigrateTree.RunAsync(newer.Services, "apply")).Exit.Should().Be(0);
        Workspace.DeleteFolder("033-newer");
        await using var older = CreateDefaultHost();

        (await MigrateTree.RunAsync(older.Services, "forget", "--above", "32", "--confirm", Database)).Exit.Should().Be(1);
        (await MigrateTree.RunAsync(older.Services, "forget", "--above", "32", "--include-contracts", "--confirm", Database)).Exit.Should().Be(0);
    }

    [Fact]
    public async Task Check_ShouldExitZero_OnACompatibleAhead()
    {
        Workspace.Write("032-older", "create table t32(id integer primary key);", "drop table t32;");
        Workspace.Write("033-newer", "create table t33(id integer primary key);", "drop table t33;");
        await using (var newer = CreateDefaultHost("0.6.0"))
            (await MigrateTree.RunAsync(newer.Services, "apply")).Exit.Should().Be(0);
        Workspace.DeleteFolder("033-newer");
        await using var older = CreateDefaultHost();

        var check = await MigrateTree.RunAsync(older.Services, "check");

        check.Exit.Should().Be(0, check.ToString());
        check.Set.State.Should().Be(MigrationLevelState.AheadCompatible);
    }

    [Fact]
    public async Task ResultLine_ShouldHoldNoConnectionStringOrPassword()
    {
        const string password = "s3cret-tree-value";
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        var unreachable = $"Host=127.0.0.1;Port=1;Database=nowhere;Username=probe;Password={password};Timeout=2";
        await using var host = MigrationSetHost.Postgres(unreachable, services => services.AddDatabaseBespokeMigrations(Workspace.Root));

        var check = await MigrateTree.RunAsync(host.Services, "check", "--json");

        check.Exit.Should().Be(2, check.ToString());
        check.ToString().Should().NotContain(password).And.NotContain("Password=");
        MigrateRunResult.FromJson(check.Output.Split('\n')[0]).Should().NotBeNull("--json prints the report before the result line");
    }

    [Fact]
    public async Task BareMigrate_ShouldApplyThenSeed_AndRerunAsCurrent()
    {
        await using var host = CreateSeededHost();

        var first = await MigrateTree.RunAsync(host.Services);
        var rerun = await MigrateTree.RunAsync(host.Services);

        first.Exit.Should().Be(0, first.ToString());
        first.Result.Verb.Should().Be(MigrateVerbs.Migrate);
        first.Set.Applied.Select(label => label.Label).Should().Equal("001-plans");
        first.Set.Seeds.Should().Equal("001-free");
        rerun.Exit.Should().Be(0);
        rerun.Set.Status.Should().Be(MigrateOutcome.Current);
        (await ScalarAsync<string>("select name from plans")).Should().Be("Free");
    }

    [Fact]
    public async Task OperatorVerbs_ShouldRefuse_WhenSeveralSetsAndNoneIsNamed()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        using var other = new MigrationsWorkspace();
        await using var host = CreateHost(services => services
            .AddDatabaseBespokeMigrations(Workspace.Root)
            .AddDatabaseBespokeMigrationSet("billing", other.Root));

        var run = await MigrateTree.RunAsync(host.Services, "repair", "--confirm", Database);

        run.Exit.Should().Be(1);
        run.Result.Message.Should().Contain("--set");
        (await MigrateTree.RunAsync(host.Services, "repair", "--set", "billing", "--confirm", Database)).Exit.Should().Be(0);
    }

    [Fact]
    public async Task UnknownSetBackgroundAndParseErrors_ShouldRefuseWithOne()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        await using var host = CreateDefaultHost();

        (await MigrateTree.RunAsync(host.Services, "check", "--set", "nope")).Exit.Should().Be(1);
        (await MigrateTree.RunAsync(host.Services, "bg", "pause", "backfill_users")).Result.Message.Should().Contain("ship later");
        (await MigrateTree.RunAsync(host.Services, "rollback", "--confirm", Database)).Exit.Should().Be(1, "--to is required");
        (await MigrateTree.RunAsync(host.Services, "claim", "--owner", "module", "--confirm", Database)).Exit.Should().Be(1, "--instance is required");
    }

    [Fact]
    public async Task MigrateKey_ShouldConnectEverySet_WithoutTheHostCallingAnything()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [MigrationConstants.ConnectionStringConfigurationKey] = Fixture.ConnectionString })
            .Build();
        await using var unreachable = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=nowhere;Username=probe;Password=probe;Timeout=2");
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<System.Data.Common.DbDataSource>(unreachable)
            .AddDataSourceConnectionFactory()
            .AddDatabaseBespokeMigrations(Workspace.Root, options => options.LocalRun = false);
        await using var provider = services.BuildServiceProvider();

        var apply = await MigrateTree.RunAsync(provider, "apply");

        apply.Exit.Should().Be(0, apply.ToString());
        (await TableExistsAsync("public", "users")).Should().BeTrue();
        provider.GetRequiredService<MigrationRunMode>().IsMigrateCommand.Should().BeTrue();
    }

    [Fact]
    public void MigrateKey_ShouldStayOff_OutsideAMigrateCommand()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [MigrationConstants.ConnectionStringConfigurationKey] = Fixture.ConnectionString })
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSqliteConnectionFactory("Data Source=:memory:")
            .AddDatabaseBespokeMigrations(Workspace.Root)
            .BuildServiceProvider();

        provider.GetRequiredService<MigrationConnectionOverride>().IsActive.Should().BeFalse();
    }

    private MigrationSetHost CreateSeededHost(Action<MigrationOptions>? configure = null)
    {
        Workspace.Write("001-plans", "create table plans(code text primary key, name text not null);", "drop table plans;");
        Directory.CreateDirectory(_seeds);
        File.WriteAllText(Path.Combine(_seeds, "001-free.json"),
            """{ "mode": "upsert", "tables": [ { "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free" } ] } ] }""");
        return CreateHost(services => services.AddDatabaseBespokeMigrationSet(
            "catalog", _ => new FileSystemMigrationBroker(Workspace.Root, _seeds), options =>
            {
                options.LocalRun = false;
                configure?.Invoke(options);
            }));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_seeds))
            Directory.Delete(_seeds, recursive: true);
    }
}
