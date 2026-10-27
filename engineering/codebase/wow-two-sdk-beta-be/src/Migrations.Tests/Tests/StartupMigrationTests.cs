using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>Applying at boot is a local-run convenience: a deployed host verifies levels read-only unless it opts in, and the deploy step applies.</summary>
public sealed class StartupMigrationTests : SqliteMigratorTestBase, IDisposable
{
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task MigrateOnStartup_ShouldApplyEverySet_OnALocalRun()
    {
        WriteSets();
        await using var host = CreateHost(Environments.Development);

        await host.Services.MigrateBespokeOnStartupAsync();

        (await host.ReadHistoryAsync("migration_history")).Should().Equal("001-orders");
        (await host.ReadHistoryAsync("identity_migration_history")).Should().Equal("001-users");
        (await host.ReadAppliedByAsync("migration_history")).Should().Equal("startup");
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Development", "dev")]
    public async Task MigrateOnStartup_ShouldApplyNothingAndThrow_WhenADeployedHostIsBehind(string environment, string? deployEnvironment)
    {
        WriteSets();
        await using var host = CreateHost(environment, deployEnvironment);

        var boot = () => host.Services.MigrateBespokeOnStartupAsync().AsTask();

        (await boot.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*migrate deploy step*001-orders*");
        (await host.HasTableAsync("public", "orders")).Should().BeFalse();
        (await host.HasTableAsync("public", "identity_users")).Should().BeFalse();
    }

    [Fact]
    public async Task MigrateOnStartup_ShouldOnlyVerify_WhenNoHostEnvironmentIsRegistered()
    {
        WriteSets();
        await using var host = CreateHost(environment: null);

        var boot = () => host.Services.MigrateBespokeOnStartupAsync().AsTask();

        await boot.Should().ThrowAsync<InvalidOperationException>();
        (await host.HasTableAsync("public", "orders")).Should().BeFalse();
    }

    [Fact]
    public async Task MigrateOnStartup_ShouldBootWithoutApplying_WhenTheDeployStepRanFirst()
    {
        WriteSets();
        await using var host = CreateHost(Environments.Production, "prod");

        var deployed = (await host.Services.MigrateBespokeForDeployAsync()).ValueOrThrow();
        await host.Services.MigrateBespokeOnStartupAsync();

        deployed.Select(set => set.Set).Should().Equal(MigrationConstants.DefaultSetName, "identity");
        (await host.ReadAppliedByAsync("migration_history")).Should().Equal("deploy");
        (await host.ReadAppliedByAsync("identity_migration_history")).Should().Equal("deploy");
        host.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information && entry.Message.Contains("read-only"));
    }

    [Fact]
    public async Task MigrateOnStartup_ShouldApplyAndWarn_WhenADeployedHostOptsIn()
    {
        WriteSets();
        await using var host = CreateHost(Environments.Production, "prod");

        await host.Services.MigrateBespokeOnStartupAsync(options => options.ApplyWhenDeployed = true);

        (await host.ReadHistoryAsync("migration_history")).Should().Equal("001-orders");
        (await host.ReadHistoryAsync("identity_migration_history")).Should().Equal("001-users");
        host.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(StartupMigrationOptions.ApplyWhenDeployed)));
    }

    [Fact]
    public async Task MigrateOnStartup_ShouldTouchNoSet_WhenTheMigratorIsDisabled()
    {
        WriteSets();
        await using var host = CreateHost(Environments.Development, register: services => services.DisableBespokeMigrator());

        await host.Services.MigrateBespokeOnStartupAsync();

        (await host.HasTableAsync("public", "orders")).Should().BeFalse();
        (await host.HasTableAsync("public", "identity_users")).Should().BeFalse();
    }

    [Fact]
    public async Task MigrateForDeploy_ShouldCreateAndApply_WhenTheHostHasNamedSetsOnly()
    {
        _identity.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
        var databasePath = Path.Combine(Path.GetTempPath(), $"named-only-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using var host = MigrationSetHost.Sqlite(connectionString, services =>
            {
                services.AddKeyedSingleton("identity", new DatabaseSettings { ConnectionString = connectionString });
                services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options => options.Provider = DatabaseProvider.Sqlite);
            });

            var deployed = (await host.Services.MigrateBespokeForDeployAsync()).ValueOrThrow();

            deployed.Select(set => set.Set).Should().Equal("identity");
            File.Exists(databasePath).Should().BeTrue();
            (await host.ReadHistoryAsync("identity_migration_history")).Should().Equal("001-users");
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _identity.Dispose();

    private void WriteSets()
    {
        Workspace.Write("001-orders", "create table orders(id integer primary key);", "drop table orders;");
        _identity.Write("001-users", "create table identity_users(id integer primary key);", "drop table identity_users;");
    }

    private MigrationSetHost CreateHost(string? environment, string? deployEnvironment = null, Action<IServiceCollection>? register = null) =>
        MigrationSetHost.Sqlite(ConnectionString, services =>
        {
            services.AddSingleton(new DatabaseSettings { ConnectionString = ConnectionString });
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
                .Build());
            if (environment is not null)
                services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));

            services.AddDatabaseBespokeMigrations(Workspace.Root, options => options.Provider = DatabaseProvider.Sqlite);
            services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options => options.Provider = DatabaseProvider.Sqlite);
            register?.Invoke(services);
        });

    /// <summary>A host environment that names only its environment.</summary>
    /// <param name="environmentName">The .NET environment name.</param>
    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Migrations.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
