using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>A set runs after the sets it depends on, refuses before any SQL while one is missing or below its level, and finds their schemas.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class DependencyAndExtensionTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture), IDisposable
{
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task Apply_ShouldRefuseBeforeAnySql_WhenTheDependencyIsBelowItsLevel()
    {
        WriteIdentity(levels: 2);
        await using (var identityImage = CreateIdentityHost())
            (await Engine(identityImage).ApplyAsync()).Exit.Should().Be(0);

        WriteOrders();
        await using var ordersImage = CreateOrdersHost(level: 3);

        var refused = await Engine(ordersImage).ApplyAsync();

        refused.Exit.Should().Be(1);
        refused.Sets.Single().MissingDependencies.Should().ContainSingle().Which.Should().Contain("found 002");
        (await TableExistsAsync("public", "orders")).Should().BeFalse();
        (await TableExistsAsync("public", "orders_migration_history")).Should().BeFalse();
        (await Engine(ordersImage).CheckAsync()).Exit.Should().Be(1);

        WriteIdentity(levels: 3);
        await using (var identityImage = CreateIdentityHost())
            (await Engine(identityImage).ApplyAsync()).Exit.Should().Be(0);

        (await Engine(ordersImage).ApplyAsync()).Exit.Should().Be(0);
        (await TableExistsAsync("public", "orders")).Should().BeTrue();
    }

    [Fact]
    public async Task Apply_ShouldRefuseBeforeAnySql_WhenTheDependencyIsMissing()
    {
        WriteOrders();
        await using var ordersImage = CreateOrdersHost(level: 1);

        var refused = await Engine(ordersImage).ApplyAsync();

        refused.Exit.Should().Be(1);
        refused.Sets.Single().MissingDependencies.Single().Should().Contain("lacks");
        (await TableExistsAsync("public", "orders")).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_ShouldRunTheReferencedSetFirst_WhenOneImageCarriesBoth()
    {
        WriteIdentity(levels: 3);
        WriteOrders();
        await using var host = CreateHost(services =>
        {
            services.AddDatabaseBespokeMigrationSet("orders", Workspace.Root, options => options.DependsOn("identity", 3));
            AddIdentity(services);
        });

        var applied = await Engine(host).ApplyAsync();

        applied.Exit.Should().Be(0);
        applied.Sets.Select(set => set.Name).Should().Equal("identity", "orders");
        (await ScalarAsync<string>(
                "select ccu.table_schema from information_schema.constraint_column_usage ccu where ccu.table_name = 'identity_users'"))
            .Should().Be("ident_dep", "{{schema:identity}} resolves through the owner marker");
    }

    [Fact]
    public void Registry_ShouldRefuse_WhenSetsDependOnEachOtherInACycle()
    {
        using var other = new MigrationsWorkspace();
        var services = new ServiceCollection();
        services.AddDatabaseBespokeMigrationSet("orders", Workspace.Root, options => options.DependsOn("billing", 1));
        services.AddDatabaseBespokeMigrationSet("billing", other.Root, options => options.DependsOn("orders", 1));
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IMigrationSetRegistry>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public async Task ApplyAndCheck_ShouldRefuseBeforeAnySql_WhenARequiredExtensionIsMissing()
    {
        Workspace.Write("001-items", "create table items(id integer primary key);", "drop table items;");
        await using var host = CreateDefaultHost(configure: options => options.RequireExtension("plpgsql").RequireExtension("wow2_absent_extension"));

        var check = await Engine(host).CheckAsync();
        var apply = await Engine(host).ApplyAsync();
        var runner = await host.Sets.GetRunner(MigrationConstants.DefaultSetName).ApplyPendingAsync("deploy");

        check.Exit.Should().Be(1);
        check.Sets.Single().MissingExtensions.Should().Equal("wow2_absent_extension");
        apply.Exit.Should().Be(1);
        runner.IsFailure(out var error, out _).Should().BeTrue();
        error!.Message.Should().Contain("wow2_absent_extension");
        (await TableExistsAsync("public", "items")).Should().BeFalse();
        (await TableExistsAsync("public", "migration_history")).Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();

    private void WriteIdentity(int levels)
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key);", "drop table {{schema}}.identity_users;");
        if (levels >= 2)
            _identity.Write("002-email", "alter table {{schema}}.identity_users add column email text;", "alter table {{schema}}.identity_users drop column email;");
        if (levels >= 3)
            _identity.Write("003-name", "alter table {{schema}}.identity_users add column name text;", "alter table {{schema}}.identity_users drop column name;");
    }

    private void WriteOrders() => Workspace.Write("001-orders",
        "create table orders(id integer primary key, user_id uuid references {{schema:identity}}.identity_users(id));",
        "drop table orders;");

    private MigrationSetHost CreateIdentityHost() => CreateHost(AddIdentity);

    private MigrationSetHost CreateOrdersHost(int level) => CreateHost(services =>
        services.AddDatabaseBespokeMigrationSet("orders", Workspace.Root, options => options.DependsOn("identity", level)));

    private void AddIdentity(IServiceCollection services) =>
        services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
        {
            options.SchemaName = "ident_dep";
            options.SchemaOwner = "service";
            options.OwnerInstance = "identity-shop@prod";
        });
}
