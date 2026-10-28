using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The read verbs write nothing, a rerun changes nothing, and the result line carries levels and kinds but never a secret.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class EngineReadAndResultTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture), IDisposable
{
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task CheckPlanAndLocks_ShouldWriteNothing_AndReadAFreshDatabaseAsBehind()
    {
        WriteSets();
        await using var host = CreateSetsHost();
        var before = await CountUserObjectsAsync();

        var check = await Engine(host).CheckAsync();
        var plan = await Engine(host).PlanAsync();
        var locks = await Engine(host).LocksAsync();

        check.Exit.Should().Be(1);
        check.Sets.Should().AllSatisfy(set => set.State.Should().Be(MigrationLevelState.Behind));
        plan.Exit.Should().Be(0);
        plan.Sets.Single(set => set.Name == "identity").Pending.Should().Equal(
            new MigrationLabel("001-users", MigrationKind.Expand), new MigrationLabel("002-drop-legacy", MigrationKind.Contract));
        locks.Exit.Should().Be(0);
        (await CountUserObjectsAsync()).Should().Be(before);
        (await ScalarAsync<bool>("select exists (select 1 from pg_namespace where nspname = 'identity_check')")).Should().BeFalse();
        (await ScalarAsync<long>("select count(*) from pg_locks where locktype = 'advisory'")).Should().Be(0);
    }

    [Fact]
    public async Task Apply_ShouldReportLevelsAndKinds_AndChangeNothingOnARerun()
    {
        WriteSets();
        await using var host = CreateSetsHost();

        var first = await Engine(host).ApplyAsync();
        var second = await Engine(host).ApplyAsync();

        first.Exit.Should().Be(0);
        var identity = first.Sets.Single(set => set.Name == "identity");
        identity.Status.Should().Be(MigrateOutcome.Done);
        identity.Schema.Should().Be("identity_check");
        identity.LevelBefore.Should().Be(0);
        identity.LevelAfter.Should().Be(2);
        identity.State.Should().Be(MigrationLevelState.Current);
        identity.RollbackFloor.Should().Be("v0.4");
        identity.Applied.Should().Equal(new MigrationLabel("001-users", MigrationKind.Expand), new MigrationLabel("002-drop-legacy", MigrationKind.Contract));
        second.Exit.Should().Be(0);
        second.Sets.Should().AllSatisfy(set => set.Status.Should().Be(MigrateOutcome.Current));
        second.Sets.Should().AllSatisfy(set => set.Applied.Should().BeEmpty());
    }

    [Fact]
    public async Task ResultLine_ShouldCarryTheVerbExitAndSets()
    {
        WriteSets();
        await using var host = CreateSetsHost();

        var line = (await Engine(host).ApplyAsync()).ToResultLine();

        line.Should().StartWith("migrate-result {\"verb\":\"apply\",\"exit\":0,\"outcome\":\"done\",\"sets\":[");
        line.Should().Contain("\"name\":\"identity\",\"schema\":\"identity_check\",\"status\":\"done\",\"state\":\"current\",\"levelBefore\":0,\"levelAfter\":2");
        line.Should().Contain("{\"label\":\"002-drop-legacy\",\"kind\":\"contract\"}");
        line.Should().Contain("\"rollbackFloor\":\"v0.4\"");
        line.Should().Contain("\"background\":[]");
    }

    [Fact]
    public async Task ResultLine_ShouldNeverCarryTheConnectionSecret_WhenTheConnectionFails()
    {
        WriteSets();
        var secret = "s3cr3t-" + Guid.NewGuid().ToString("N")[..8];
        var wrong = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Password = secret, Pooling = false }.ConnectionString;
        await using var host = CreateHost(services =>
        {
            services.AddDatabaseBespokeMigrations(Workspace.Root);
            services.AddMigrationConnectionOverride(wrong);
        });

        var result = await Engine(host).ApplyAsync();

        result.Exit.Should().Be(2);
        result.ToResultLine().Should().NotContain(secret);
    }

    [Fact]
    public async Task ConnectionOverride_ShouldConnectEverySet_InPlaceOfTheHostConnection()
    {
        WriteSets();
        var unreachable = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = "no_such_database", Pooling = false }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(unreachable);
        await using var host = MigrationSetHost.Postgres(Fixture.ConnectionString, services =>
        {
            RegisterSets(services);
            services.AddKeyedSingleton<IDbConnectionFactory>("identity", new DataSourceConnectionFactory(dataSource));
            services.AddMigrationConnectionOverride(Fixture.ConnectionString);
        });

        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);
        (await TableExistsAsync("identity_check", "users")).Should().BeTrue();
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();

    private void WriteSets()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users", "create table {{schema}}.users(id integer primary key, legacy text);", "drop table {{schema}}.users;");
        _identity.Write("002-drop-legacy", "-- @contract since=v0.4\nalter table {{schema}}.users drop column legacy;", "alter table {{schema}}.users add column legacy text;");
    }

    private MigrationSetHost CreateSetsHost() => CreateHost(RegisterSets);

    private void RegisterSets(IServiceCollection services)
    {
        services.AddDatabaseBespokeMigrations(Workspace.Root, options => options.Release = "0.5.0");
        services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
        {
            options.SchemaName = "identity_check";
            options.SchemaOwner = "module";
            options.Release = "0.5.0";
        });
    }

    private Task<long> CountUserObjectsAsync() => ScalarAsync<long>(
        """
        select (select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace
                where n.nspname not in ('pg_catalog', 'information_schema') and n.nspname not like 'pg_toast%')
             + (select count(*) from pg_namespace)
        """);
}
