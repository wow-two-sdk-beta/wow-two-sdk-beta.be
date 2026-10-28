using AwesomeAssertions;
using Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>A seed file fills every table it names in one transaction, is journalled once, keeps its mode, and refuses once changed.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class SeedTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture), IDisposable
{
    private readonly string _seeds = Path.Combine(Path.GetTempPath(), "wow2-seed-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Seed_ShouldApplyOnce_AndBindEveryValueToItsColumnType()
    {
        WriteSchema();
        WriteSeed("001-plans", "upsert", """
            { "table": "plans", "key": ["code"], "rows": [
                { "code": "free", "name": "Free", "max_codes": 3, "meta": { "tier": 0 }, "public_id": "0190f3a5-7c1e-7d2a-9b1f-3c4d5e6f7a8b", "starts_on": "2026-01-01" } ] },
            { "table": "features", "key": ["plan_code", "label"], "rows": [ { "plan_code": "free", "label": "qr" } ] }
            """);
        await using var host = CreateSeededHost();
        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);

        var first = await Engine(host).SeedAsync();
        var second = await Engine(host).SeedAsync();

        first.Exit.Should().Be(0);
        first.Sets.Single().Seeds.Should().Equal("001-plans");
        second.Sets.Single().Status.Should().Be(MigrateOutcome.Current);
        (await ScalarAsync<string>("select name || ' ' || max_codes || ' ' || (meta->>'tier') || ' ' || starts_on from plans")).Should().Be("Free 3 0 2026-01-01");
        (await ScalarAsync<long>("select count(*) from features")).Should().Be(1);
        (await ScalarAsync<string>("select name || ':' || mode from seed_history")).Should().Be("001-plans:upsert");
    }

    [Fact]
    public async Task Seed_ShouldWriteNothingFromAFile_WhenAnyOfItsTablesFails()
    {
        WriteSchema();
        WriteSeed("001-broken", "upsert", """
            { "table": "plans", "key": ["code"], "rows": [ { "code": "pro", "name": "Pro", "max_codes": 50 } ] },
            { "table": "features", "key": ["plan_code", "label"], "rows": [ { "plan_code": "missing", "label": "api" } ] }
            """);
        await using var host = CreateSeededHost();
        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);

        var failed = await Engine(host).SeedAsync();

        failed.Exit.Should().Be(2);
        failed.Sets.Single().SqlState.Should().Be("23503");
        (await ScalarAsync<long>("select count(*) from plans")).Should().Be(0);
        (await TableExistsAsync("public", "seed_history")).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_ShouldRefuse_WhenAnAppliedFileChanged()
    {
        WriteSchema();
        WriteSeed("001-plans", "upsert", """{ "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free", "max_codes": 3 } ] }""");
        await using var host = CreateSeededHost();
        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);
        (await Engine(host).SeedAsync()).Exit.Should().Be(0);

        WriteSeed("001-plans", "upsert", """{ "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free", "max_codes": 5 } ] }""");
        var refused = await Engine(host).SeedAsync();

        refused.Exit.Should().Be(1);
        refused.Sets.Single().Message.Should().Contain("001-plans");
        (await ScalarAsync<int>("select max_codes from plans")).Should().Be(3);
    }

    [Fact]
    public async Task Seed_ShouldLeaveARuntimeRowAlone_InInsertMode_AndOverwriteIt_InUpsertMode()
    {
        WriteSchema();
        WriteSeed("001-plans", "insert", """{ "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free", "max_codes": 3 } ] }""");
        await using (var host = CreateSeededHost())
        {
            (await Engine(host).ApplyAsync()).Exit.Should().Be(0);
            (await Engine(host).SeedAsync()).Exit.Should().Be(0);
        }

        await ExecuteAsync("update plans set name = 'Edited by the owner'");
        WriteSeed("002-plans-again", "insert", """{ "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free", "max_codes": 3 } ] }""");
        await using (var host = CreateSeededHost())
            (await Engine(host).SeedAsync()).Exit.Should().Be(0);
        (await ScalarAsync<string>("select name from plans")).Should().Be("Edited by the owner");

        WriteSeed("003-plans-wins", "upsert", """{ "table": "plans", "key": ["code"], "rows": [ { "code": "free", "name": "Free", "max_codes": 3 } ] }""");
        await using (var host = CreateSeededHost())
            (await Engine(host).SeedAsync()).Exit.Should().Be(0);
        (await ScalarAsync<string>("select name from plans")).Should().Be("Free");
    }

    [Fact]
    public async Task Seed_ShouldRefuseAMalformedFile_BeforeAnyWrite()
    {
        WriteSchema();
        WriteSeed("001-plans", "replace", """{ "table": "plans", "key": ["code"], "rows": [] }""");
        await using var host = CreateSeededHost();
        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);

        var refused = await Engine(host).SeedAsync();

        refused.Exit.Should().Be(1);
        refused.Sets.Single().Message.Should().Contain("mode");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_seeds))
                Directory.Delete(_seeds, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a leaked temp dir must never fail a test.
        }
    }

    private void WriteSchema() => Workspace.Write("001-plans",
        """
        create table plans(code varchar(20) primary key, name text not null, max_codes integer not null, meta jsonb, public_id uuid, starts_on date);
        create table features(plan_code varchar(20) not null references plans(code), label text not null, primary key (plan_code, label));
        """,
        "drop table features; drop table plans;");

    private void WriteSeed(string name, string mode, string tables)
    {
        Directory.CreateDirectory(_seeds);
        File.WriteAllText(Path.Combine(_seeds, name + ".json"), $$"""{ "mode": "{{mode}}", "tables": [ {{tables}} ] }""");
    }

    private MigrationSetHost CreateSeededHost() => CreateHost(services =>
        services.AddDatabaseBespokeMigrationSet("catalog", _ => new FileSystemMigrationBroker(Workspace.Root, _seeds), options =>
        {
            options.TableName = "migration_history";
            options.LocalRun = false;
        }));
}
