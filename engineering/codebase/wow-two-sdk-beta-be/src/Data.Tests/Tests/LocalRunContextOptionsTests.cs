using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Tests.Harness;

namespace WoW.Two.Sdk.Beta.Be.Data.Tests.Tests;

/// <summary>
/// EF Core sensitive-data logging and detailed errors default on only for a local run: deployed dev runs as Development
/// and must keep parameter values out of its logs. <c>AddPostgresPersistence</c> lets a product override either.
/// The cases read the built options only, so no database is opened.
/// </summary>
public sealed class LocalRunContextOptionsTests
{
    // Never opened: building DbContextOptions does not connect.
    private const string ConnectionString = "Host=localhost;Database=local_run_options;Username=none;Password=none";

    private const string UnusedConnectionEnvironmentVariable = "WOW_TWO_DATA_TESTS_NO_SUCH_ENV_VAR";

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Development", "dev", false)]
    [InlineData("Production", "prod", false)]
    [InlineData("Production", null, false)]
    public void PostgresPersistence_DefaultsToLocalRunOnly(string environment, string? deployEnvironment, bool expected)
    {
        using var provider = BuildPostgres(environment, deployEnvironment);

        var core = CoreOptions(provider);

        core.IsSensitiveDataLoggingEnabled.Should().Be(expected);
        core.DetailedErrorsEnabled.Should().Be(expected);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Development", "dev", false)]
    [InlineData("Production", null, false)]
    public void EntityFrameworkCore_DefaultsToLocalRunOnly(string environment, string? deployEnvironment, bool expected)
    {
        var services = HostServices(environment, deployEnvironment);
        services.AddEntityFrameworkCore<DataTestDbContext>(
            options => options.UsePooling = false,
            builder => builder.UseNpgsql(ConnectionString));
        using var provider = services.BuildServiceProvider();

        var core = CoreOptions(provider);

        core.IsSensitiveDataLoggingEnabled.Should().Be(expected);
        core.DetailedErrorsEnabled.Should().Be(expected);
    }

    [Fact]
    public void PostgresPersistence_FlagsOverrideTheLocalRunDefault()
    {
        using var local = BuildPostgres("Development", null, options =>
        {
            options.EnableSensitiveDataLogging = false;
            options.EnableDetailedErrors = false;
        });
        using var deployedDev = BuildPostgres("Development", "dev", options => options.EnableDetailedErrors = true);

        CoreOptions(local).IsSensitiveDataLoggingEnabled.Should().BeFalse();
        CoreOptions(local).DetailedErrorsEnabled.Should().BeFalse();
        CoreOptions(deployedDev).IsSensitiveDataLoggingEnabled.Should().BeFalse();
        CoreOptions(deployedDev).DetailedErrorsEnabled.Should().BeTrue();
    }

    [Fact]
    public void PostgresPersistence_ConfigureDbContextRunsLastAndWins()
    {
        var seen = false;
        using var provider = BuildPostgres("Development", null, options =>
        {
            options.NoTrackingByDefault = true;
            options.ConfigureDbContext = (services, builder) =>
            {
                seen = services.GetService<IHostEnvironment>() is not null;
                builder.EnableSensitiveDataLogging(false);
            };
        });

        var built = Options(provider);

        seen.Should().BeTrue();
        built.FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled.Should().BeFalse(); // after the local-run default, so the hook wins
        built.FindExtension<CoreOptionsExtension>()!.QueryTrackingBehavior.Should().Be(QueryTrackingBehavior.NoTracking);
    }

    private static ServiceProvider BuildPostgres(
        string environment, string? deployEnvironment, Action<PostgresPersistenceOptions>? configure = null)
    {
        var services = HostServices(environment, deployEnvironment);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DatabaseSettings:ConnectionString"] = ConnectionString })
            .Build();
        services.AddPostgresPersistence<DataTestDbContext>(configuration, options =>
        {
            options.ConnectionStringEnvironmentVariable = UnusedConnectionEnvironmentVariable;
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider();
    }

    private static ServiceCollection HostServices(string environment, string? deployEnvironment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
            .Build());
        return services;
    }

    private static DbContextOptions<DataTestDbContext> Options(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DbContextOptions<DataTestDbContext>>();
    }

    private static CoreOptionsExtension CoreOptions(ServiceProvider provider)
        => Options(provider).FindExtension<CoreOptionsExtension>()!;

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "WoW2.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
