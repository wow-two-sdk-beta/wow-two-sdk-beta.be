using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.DbUp;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.DbUp.Extensions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Ef;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.TestData.Ef;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>EF and DbUp sets answer the same tree: check, plan, apply, seed and sets with the bespoke exits; every other verb refuses them.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class StrategySetTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture)
{
    private string Database => new NpgsqlConnectionStringBuilder(Fixture.ConnectionString).Database!;

    [Fact]
    public async Task EfSet_ShouldCheckApplyAndDescribe_ThroughTheTree()
    {
        await using var provider = EfHost();

        var behind = await MigrateTree.RunAsync(provider, "check");
        var plan = await MigrateTree.RunAsync(provider, "plan");
        var apply = await MigrateTree.RunAsync(provider, "apply");
        var current = await MigrateTree.RunAsync(provider, "check");
        var rerun = await MigrateTree.RunAsync(provider);
        var sets = await MigrateTree.RunAsync(provider, "sets", "--json");

        behind.Exit.Should().Be(1, behind.ToString());
        behind.Set.State.Should().Be(MigrationLevelState.Behind);
        plan.Set.Pending.Select(label => label.Label).Should().Equal("20260101000000_Items");
        apply.Exit.Should().Be(0, apply.ToString());
        apply.Set.Applied.Select(label => label.Label).Should().Equal("20260101000000_Items");
        current.Exit.Should().Be(0, current.ToString());
        rerun.Exit.Should().Be(0, rerun.ToString());
        rerun.Set.Status.Should().Be(MigrateOutcome.Current);
        sets.Output.Should().Contain("\"strategy\":\"ef\"").And.Contain("\"level\":1");
        (await TableExistsAsync("public", "ef_probe_items")).Should().BeTrue();
    }

    [Fact]
    public async Task EfSet_ShouldRefuseTheVerbsItsRunnerCannotServe()
    {
        await using var provider = EfHost();

        foreach (var verb in new[] { new[] { "apply", "--dry-run" }, ["rollback", "--to", "0", "--confirm", Database], ["locks"], ["unlock", "--confirm", Database] })
        {
            var run = await MigrateTree.RunAsync(provider, verb);
            run.Exit.Should().Be(1, $"{string.Join(' ', verb)}: {run}");
            (run.Result.Message ?? run.Set.Message).Should().Contain("ef");
        }
    }

    [Fact]
    public async Task EfSetBesideABespokeSet_ShouldApplyBothInOneRun()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        await using var provider = EfHost(services => services.AddDatabaseBespokeMigrations(Workspace.Root, options => options.LocalRun = false));

        var apply = await MigrateTree.RunAsync(provider, "apply");
        var check = await MigrateTree.RunAsync(provider, "check", "--set", "shop");

        apply.Exit.Should().Be(0, apply.ToString());
        apply.Result.Sets.Select(set => set.Name).Should().Equal("default", "shop");
        check.Exit.Should().Be(0, check.ToString());
        check.Set.Name.Should().Be("shop");
    }

    [Fact]
    public async Task DbUpSet_ShouldCheckApplyAndDescribe_ThroughTheTree()
    {
        var services = new ServiceCollection().AddLogging().AddDbUpRunner(Fixture.ConnectionString, options =>
        {
            options.UsePostgres();
            options.ScriptsAssembly = typeof(StrategySetTests).Assembly;
            options.ScriptsNamespacePrefix = "DbUpScripts.";
            options.SetName = "legacy";
        });
        await using var provider = services.BuildServiceProvider();

        var behind = await MigrateTree.RunAsync(provider, "check");
        var apply = await MigrateTree.RunAsync(provider, "apply");
        var current = await MigrateTree.RunAsync(provider, "check");
        var rerun = await MigrateTree.RunAsync(provider, "apply");
        var dryRun = await MigrateTree.RunAsync(provider, "apply", "--dry-run");
        var sets = await MigrateTree.RunAsync(provider, "sets", "--json");

        behind.Exit.Should().Be(1, behind.ToString());
        behind.Set.Pending.Should().HaveCount(2);
        apply.Exit.Should().Be(0, apply.ToString());
        apply.Set.Applied.Select(label => label.Label).Should().Equal("DbUpScripts.001-items.sql", "DbUpScripts.002-names.sql");
        current.Exit.Should().Be(0, current.ToString());
        rerun.Set.Status.Should().Be(MigrateOutcome.Current);
        dryRun.Exit.Should().Be(1);
        sets.Output.Should().Contain("\"name\":\"legacy\"").And.Contain("\"strategy\":\"dbup\"").And.Contain("\"level\":2");
        (await TableExistsAsync("public", "dbup_probe_items")).Should().BeTrue();
    }

    private ServiceProvider EfHost(Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<ProbeContext>(options => options
                .UseNpgsql(Fixture.ConnectionString)
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)))
            .AddEfMigrationsRunner<ProbeContext>(options => options.SetName = "shop");
        if (register is not null)
        {
            services.AddSingleton<System.Data.Common.DbDataSource>(_ => NpgsqlDataSource.Create(Fixture.ConnectionString));
            services.AddDataSourceConnectionFactory();
            register(services);
        }

        return services.BuildServiceProvider();
    }
}
