using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Meta;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Meta;

/// <summary>
/// The service host dispatches a <c>Program.cs</c>'s verbs: a pre-host verb before any builder, a host verb on the built
/// services without serving, built-in <c>migrate</c> and <c>config</c>; any other first argument serves.
/// </summary>
public sealed class ServiceHostTests
{
    [Fact]
    public async Task PreHostVerb_ShouldRunBeforeAnyBuilder_AndOwnStdout()
    {
        var run = new HostRun();
        string[]? seen = null;

        var exit = await run.RunAsync(["keys", "generate", "--role", "signing"], verbs => verbs.AddPreHostVerb("keys", verb =>
        {
            seen = verb.Arguments;
            verb.Output.Write("-----BEGIN CERTIFICATE-----");
            verb.Error.WriteLine("Generated a signing certificate");
            return 7;
        }));

        exit.Should().Be(7, "the verb's exit is the process's");
        seen.Should().Equal("generate", "--role", "signing");
        run.Output.ToString().Should().Be("-----BEGIN CERTIFICATE-----");
        run.BuilderConfigured.Should().BeFalse("a pre-host verb runs before any builder exists");
        run.AppConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task HostVerb_ShouldRunOnTheBuiltServices_WithoutServing()
    {
        var run = new HostRun();

        var exit = await run.RunAsync(["reindex", "--all"], verbs => verbs.AddHostVerb("reindex", (services, verb, _) =>
        {
            verb.Output.WriteLine($"{services.GetRequiredService<Marker>().Name} {string.Join(' ', verb.Arguments)}");
            return Task.FromResult(3);
        }));

        exit.Should().Be(3);
        run.Output.ToString().Should().Be($"registered --all{Environment.NewLine}");
        run.BuilderConfigured.Should().BeTrue();
        run.AppConfigured.Should().BeFalse("a host verb answers before Configure");
        run.Probe!.Started.Should().BeFalse("a host verb starts no hosted service");
    }

    [Fact]
    public async Task Migrate_ShouldAnswerTheBuiltInVerb_WithItsResultLineLast()
    {
        var run = new HostRun();

        var exit = await run.RunAsync(["migrate", "sets", "--json"]);

        var result = MigrateRunResult.FromOutput(run.Output.ToString());
        result.Should().NotBeNull(run.Output + run.Error.ToString());
        exit.Should().Be(result!.Exit);
        result.Verb.Should().Be("sets");
        run.Output.ToString().TrimEnd().Split('\n')[^1].Should().StartWith(MigrateRunResult.ResultLinePrefix);
        run.AppConfigured.Should().BeFalse();
        run.Probe!.Started.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigCheck_ShouldAnswerTheBuiltInVerb_NamedAfterTheService()
    {
        var run = new HostRun(settings: new() { ["Shop:MaxItems"] = "5" });

        var exit = await run.RunAsync(["config", "check"]);

        exit.Should().Be(0, run.Error.ToString());
        var result = ConfigCheckResult.FromOutput(run.Output.ToString())!;
        result.Service.Should().Be("shop");
        result.Invalid.Should().BeEmpty();
        run.Output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle("stdout carries the result line only");
        run.AppConfigured.Should().BeFalse();
        run.Probe!.Started.Should().BeFalse("a check starts no hosted service");
    }

    [Fact]
    public async Task ConfigCheck_ShouldRefuseAnUnknownKey_WithExitOne()
    {
        var run = new HostRun(settings: new() { ["Shop:MaxItemz"] = "5" });

        var exit = await run.RunAsync(["config", "check"]);

        exit.Should().Be(1);
        ConfigCheckResult.FromOutput(run.Output.ToString())!.Unknown.Should().Equal("Shop:MaxItemz");
    }

    [Theory]
    [InlineData]
    [InlineData("serve-please")]
    [InlineData("--urls", "http://127.0.0.1:0")]
    public async Task OtherFirstArguments_ShouldServe(params string[] args)
    {
        var run = new HostRun();

        var exit = await run.RunAsync(args, verbs => verbs.AddPreHostVerb("keys", _ => 9));

        exit.Should().Be(0);
        run.AppConfigured.Should().BeTrue();
        run.Probe!.Started.Should().BeTrue("a serving boot starts its hosted services");
        run.Output.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData(2, "config")]
    [InlineData(2, "config", "verify")]
    [InlineData(1, "migrate", "nonsense")]
    public async Task KnownVerb_WithWrongArguments_ShouldAnswerItsOwnUsage_AndNeverServe(int expected, params string[] args)
    {
        var run = new HostRun();

        var exit = await run.RunAsync(args);

        exit.Should().Be(expected, run.Error.ToString());
        run.AppConfigured.Should().BeFalse();
        run.Probe!.Started.Should().BeFalse();
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("config")]
    [InlineData("--keys")]
    [InlineData("two words")]
    public async Task AddingAReservedOrMalformedVerb_ShouldThrow(string name)
    {
        var act = () => new HostRun().RunAsync(["keys"], verbs => verbs.AddPreHostVerb(name, _ => 0));

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(name));
    }

    [Fact]
    public async Task AddingAVerbTwice_ShouldThrow()
    {
        var act = () => new HostRun().RunAsync(["keys"], verbs => verbs
            .AddPreHostVerb("keys", _ => 0)
            .AddHostVerb("keys", (_, _, _) => Task.FromResult(0)));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*registered already*");
    }

    [Fact]
    public async Task DeployedBoot_WithAFinding_ShouldRefuseBeforeConfigure()
    {
        var run = new HostRun(settings: new() { ["Shop:MaxItemz"] = "5", [LocalRunExtensions.DeployEnvironmentVariable] = "dev" });

        var exit = await run.RunAsync([]);

        exit.Should().Be(1, "a deployed boot refuses an unknown key");
        run.AppConfigured.Should().BeFalse();
        run.Probe!.Started.Should().BeFalse();
    }

    [Fact]
    public async Task LocalBoot_WithAFinding_ShouldLogAndServe()
    {
        var run = new HostRun(settings: new() { ["Shop:MaxItemz"] = "5" });

        var exit = await run.RunAsync([]);

        exit.Should().Be(0);
        run.AppConfigured.Should().BeTrue("a run without DEPLOY_ENVIRONMENT logs the finding and serves");
    }

    /// <summary>One <see cref="ServiceHost"/> run over a TestServer host that stops as soon as it has started.</summary>
    private sealed class HostRun(Dictionary<string, string?>? settings = null)
    {
        public StringWriter Output { get; } = new();

        public StringWriter Error { get; } = new();

        public bool BuilderConfigured { get; private set; }

        public bool AppConfigured { get; private set; }

        public StartProbe? Probe { get; private set; }

        public Task<int> RunAsync(string[] args, Action<ServiceVerbs>? verbs = null) =>
            ServiceHost.RunAsync(args, ConfigureBuilder, ConfigureAppAsync, verbs, Output, Error, CancellationToken.None);

        private void ConfigureBuilder(WebApplicationBuilder builder)
        {
            BuilderConfigured = true;
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings ?? []);
            builder.Services.AddSingleton(new ApiDefaultsOptions { ServiceName = "shop" });
            builder.Services.AddSingleton(new Marker("registered"));
            builder.Services.AddValidatedSettings<ShopLimits>(builder.Configuration, "Shop", _ => { });
            var probe = new StartProbe();
            Probe = probe;
            builder.Services.AddHostedService(_ => probe);
        }

        private Task ConfigureAppAsync(WebApplication app)
        {
            AppConfigured = true;
            app.Lifetime.ApplicationStarted.Register(app.Lifetime.StopApplication);
            return Task.CompletedTask;
        }
    }

    /// <summary>A service the builder registers.</summary>
    private sealed record Marker(string Name);

    /// <summary>The test service's own settings, bound from <c>Shop</c>.</summary>
    private sealed class ShopLimits
    {
        public int MaxItems { get; set; } = 10;
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
