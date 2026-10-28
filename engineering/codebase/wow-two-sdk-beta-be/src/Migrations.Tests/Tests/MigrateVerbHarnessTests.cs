using AwesomeAssertions;
using Dapper;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The image E2E verb harness: a second run of every verb exits 0 and changes nothing, and <c>check</c> writes nothing.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class MigrateVerbHarnessTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture), IDisposable
{
    private readonly string _seeds = Path.Combine(Path.GetTempPath(), "wow2-verb-seeds", Guid.NewGuid().ToString("N"));

    private string Database => new NpgsqlConnectionStringBuilder(Fixture.ConnectionString).Database!;

    [Fact]
    public async Task Check_ShouldWriteNothing_OnAnEmptyAndOnACurrentDatabase()
    {
        await using var host = CreateSeededHost();
        var verbs = new MigrateVerbHarness(host.Services);

        (await verbs.AssertCheckWritesNothingAsync()).Exit.Should().Be(1);
        await verbs.AssertExitAsync(0);
        (await verbs.AssertCheckWritesNothingAsync()).Exit.Should().Be(0);
    }

    [Fact]
    public async Task ApplySeedAndTheBareMigrate_ShouldRerunWithoutAChange()
    {
        await using var host = CreateSeededHost();
        var verbs = new MigrateVerbHarness(host.Services);

        await verbs.AssertRerunChangesNothingAsync("apply", "--dry-run");
        await verbs.AssertRerunChangesNothingAsync("apply");
        await verbs.AssertRerunChangesNothingAsync("seed");
        await verbs.AssertRerunChangesNothingAsync();
        foreach (var read in new[] { "check", "plan", "locks", "sets" })
            await verbs.AssertRerunChangesNothingAsync(read);
    }

    [Fact]
    public async Task OperatorVerbs_ShouldRerunWithoutAChange()
    {
        Workspace.Write("001-users", "create table if not exists {{schema}}.users(id integer primary key);", "drop table if exists {{schema}}.users;");
        Workspace.Write("002-orders", "create table if not exists {{schema}}.orders(id integer primary key);", "drop table if exists {{schema}}.orders;");
        await using var host = CreateHost(services => services.AddDatabaseBespokeMigrationSet("accounts", Workspace.Root, options =>
        {
            options.SchemaName = "accounts";
            options.SchemaOwner = "module";
            options.OwnerInstance = "shop@test";
            options.LocalRun = false;
            options.AdvisoryLockWait = TimeSpan.FromSeconds(1);
        }));
        var verbs = new MigrateVerbHarness(host.Services);
        await verbs.AssertExitAsync(0, "apply");

        await verbs.AssertRerunChangesNothingAsync("rollback", "--to", "1", "--confirm", Database);
        await verbs.AssertRerunChangesNothingAsync("forget", "--above", "0", "--confirm", Database);
        await verbs.AssertRerunChangesNothingAsync("baseline", "--to", "1", "--confirm", Database);
        Workspace.OverwriteApply("001-users", "create table if not exists {{schema}}.users(id integer primary key); -- reworded");
        await verbs.AssertRerunChangesNothingAsync("repair", "--confirm", Database);

        var key = MigrationLockKey.Reserved(host.Sets.GetOptions("accounts").AdvisoryLockId);
        await using (var holder = await OpenAsync("hung-migrator"))
        {
            await holder.ExecuteAsync("select pg_advisory_lock(@keyClass, @key)", new { keyClass = key.KeyClass, key = (int)key.Key });
            await verbs.AssertRerunChangesNothingAsync("unlock", "--confirm", Database);
        }

        await verbs.AssertRerunChangesNothingAsync("claim", "--owner", "module", "--instance", "shop@prod", "--confirm", Database);
        (await ReadEventsAsync("accounts.accounts_migration_history_events")).Select(row => row.Verb)
            .Should().Equal("rollback", "forget", "baseline", "repair", "unlock", "claim");
    }

    [Fact]
    public async Task Harness_ShouldThrow_WhenAnExitDiffers()
    {
        await using var host = CreateSeededHost();
        var verbs = new MigrateVerbHarness(host.Services);

        var wrong = () => verbs.AssertExitAsync(0, "check");

        (await wrong.Should().ThrowAsync<MigrateVerbAssertionException>()).WithMessage("*expected exit 0*");
    }

    [Fact]
    public async Task Harness_ShouldThrow_WhenTheDatabaseChangesAroundACheck()
    {
        await using var host = CreateSeededHost();
        await new MigrateVerbHarness(host.Services).AssertExitAsync(0);
        var snapshots = 0;
        var verbs = new MigrateVerbHarness(host.Services, async ct =>
        {
            var connection = await OpenAsync();
            if (++snapshots == 2)
                await connection.ExecuteAsync("insert into plans(code, name) values ('pro', 'Pro')");
            return connection;
        });

        var check = () => verbs.AssertCheckWritesNothingAsync();

        (await check.Should().ThrowAsync<MigrateVerbAssertionException>()).WithMessage("*check wrote*rows public.plans*");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_seeds))
            Directory.Delete(_seeds, recursive: true);
    }

    private MigrationSetHost CreateSeededHost()
    {
        Workspace.Write("001-plans", "create table plans(code text primary key, name text not null);", "drop table plans;");
        Directory.CreateDirectory(_seeds);
        File.WriteAllText(Path.Combine(_seeds, "001-free.json"),
            """{ "mode": "upsert", "tables": [ { "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free" } ] } ] }""");
        return CreateHost(services => services.AddDatabaseBespokeMigrationSet(
            "catalog", _ => new FileSystemMigrationBroker(Workspace.Root, _seeds), options => options.LocalRun = false));
    }
}
