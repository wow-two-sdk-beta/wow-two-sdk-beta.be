using System.Data.Common;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The host helper answers <c>app migrate &lt;verb&gt;</c> from a built web app: the exit comes back and the server never starts.</summary>
/// <remarks>Runs in the serial Postgres collection, so swapping the process's console stays local to one test.</remarks>
[Collection(PostgresMigrationCollection.Name)]
public sealed class MigrateCommandHostTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture), IDisposable
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("wow2-host-").FullName;

    [Fact]
    public async Task SetsJson_ShouldDescribeEverySet_WithNoSettingsFileAndNoDatabase()
    {
        var builder = CreateBuilder();
        builder.Services.AddDatabaseBespokeMigrationSet(
            "identity", _ => new EmbeddedResourceMigrationBroker(typeof(MigrateCommandHostTests).Assembly, "SetMigrations/identity/"), options =>
            {
                options.SchemaName = "identity";
                options.SchemaOwner = "module";
                options.RequireExtension("pg_trgm");
                options.AddReferenceableTable("identity_embedded_users", keys: ["id"]);
            });
        await using var app = builder.Build();

        var (exit, output, error) = await RunAsync(app, "migrate", "sets", "--json");

        exit.Should().Be(0, output + error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[^1].Should().StartWith(MigrateRunResult.ResultLinePrefix);
        using var document = JsonDocument.Parse(lines[0]);
        var set = document.RootElement.GetProperty("sets").EnumerateArray().Single();
        set.GetProperty("name").GetString().Should().Be("identity");
        set.GetProperty("schema").GetString().Should().Be("identity");
        set.GetProperty("ownerKind").GetString().Should().Be("module");
        set.GetProperty("level").GetInt32().Should().Be(1);
        set.GetProperty("extensions").EnumerateArray().Select(item => item.GetString()).Should().Equal("pg_trgm");
        set.GetProperty("package").GetString().Should().Be(typeof(MigrateCommandHostTests).Assembly.GetName().Name);
        AssertNeverStarted(app);
    }

    [Fact]
    public async Task Verbs_ShouldReturnTheirExit_WithoutStartingTheServer()
    {
        Workspace.Write("001-users", "create table users(id integer primary key);", "drop table users;");
        var builder = CreateBuilder();
        builder.Services
            .AddSingleton<DbDataSource>(_ => NpgsqlDataSource.Create(Fixture.ConnectionString))
            .AddDataSourceConnectionFactory()
            .AddDatabaseBespokeMigrations(Workspace.Root, options => options.LocalRun = false);
        await using var app = builder.Build();

        var behind = await RunAsync(app, "migrate", "check");
        var migrate = await RunAsync(app, "migrate");
        var current = await RunAsync(app, "migrate", "check");

        behind.Exit.Should().Be(1, behind.Output + behind.Error);
        migrate.Exit.Should().Be(0, migrate.Output + migrate.Error);
        current.Exit.Should().Be(0, current.Output + current.Error);
        MigrateRunResult.FromOutput(migrate.Output)!.Sets.Single().Applied.Select(label => label.Label).Should().Equal("001-users");
        (await TableExistsAsync("public", "users")).Should().BeTrue();
        AssertNeverStarted(app);
    }

    [Fact]
    public void IsCommand_ShouldSelectOnlyMigrateFirst()
    {
        MigrateCommand.IsCommand(["migrate"]).Should().BeTrue();
        MigrateCommand.IsCommand(["migrate", "apply"]).Should().BeTrue();
        MigrateCommand.IsCommand([]).Should().BeFalse();
        MigrateCommand.IsCommand(["--urls", "migrate"]).Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    private WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = _contentRoot, EnvironmentName = Environments.Production });
        builder.Services.AddSingleton<StartProbe>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<StartProbe>());
        return builder;
    }

    private static void AssertNeverStarted(WebApplication app)
    {
        app.Services.GetRequiredService<StartProbe>().Started.Should().BeFalse("a migrate run starts no hosted service");
        app.Lifetime.ApplicationStarted.IsCancellationRequested.Should().BeFalse("a migrate run never starts the server");
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(WebApplication app, params string[] args)
    {
        var (originalOutput, originalError) = (Console.Out, Console.Error);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exit = await app.RunMigrateCommandAsync(args);
            return (exit, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    /// <summary>Records whether the host started its hosted services.</summary>
    private sealed class StartProbe : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
