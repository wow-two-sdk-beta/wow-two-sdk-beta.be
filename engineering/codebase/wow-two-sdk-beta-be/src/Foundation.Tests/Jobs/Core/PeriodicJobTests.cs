using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Jobs.Core;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.BackgroundServices;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Jobs.Core;

/// <summary>Periodic jobs run each pass in a fresh scope, survive a failed or hung pass, stop with the host and stay off at a zero interval.</summary>
public sealed class PeriodicJobTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Passes_RunOnTheIntervalEachInAFreshScope()
    {
        var probe = new JobProbe();
        var clock = new FakeTimeProvider();
        using IHost host = await StartAsync(probe, clock, options => options.Interval = Interval);

        await AdvanceUntilAsync(clock, () => probe.Passes >= 3);

        probe.Instances.Distinct().Should().HaveCount(probe.Passes, "every pass resolves the scoped service from its own scope");
    }

    [Fact]
    public async Task FirstPass_RunsAtOnceAndTheSecondWaitsForTheInterval()
    {
        var probe = new JobProbe();
        var clock = new FakeTimeProvider();
        using IHost host = await StartAsync(probe, clock, options => options.Interval = Interval);

        await WaitUntilAsync(() => probe.Passes == 1);
        await Task.Delay(100);
        probe.Passes.Should().Be(1, "no time has passed on the job's clock");

        await AdvanceUntilAsync(clock, () => probe.Passes == 2);
    }

    [Fact]
    public async Task ZeroInterval_LeavesTheJobOff()
    {
        var probe = new JobProbe();
        using IHost host = await StartAsync(probe, new FakeTimeProvider(), options => options.Interval = TimeSpan.Zero);

        await WaitUntilAsync(() => Job(host).ExecuteTask is { IsCompleted: true });
        probe.Passes.Should().Be(0);
    }

    [Fact]
    public async Task Interval_IsReadWhenTheHostStartsSoLaterConfigurationCounts()
    {
        var probe = new JobProbe();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<ProbeService>();
        builder.Services.AddSingleton(probe);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        builder.Services.AddPeriodicJob<ProbeService>(
            "probe",
            (service, ct) => service.RunAsync(ct),
            (options, services) => options.Interval =
                TimeSpan.FromSeconds(services.GetRequiredService<IConfiguration>().GetValue("Probe:Seconds", 30)));
        builder.Configuration.AddInMemoryCollection([new("Probe:Seconds", "0")]);
        using IHost host = builder.Build();
        await host.StartAsync();

        await WaitUntilAsync(() => Job(host).ExecuteTask is { IsCompleted: true });
        probe.Passes.Should().Be(0, "a test override applied after registration turns the job off");
    }

    [Fact]
    public async Task FailedPass_IsLoggedAndTheNextPassRuns()
    {
        var probe = new JobProbe { FailFirstPass = true };
        var clock = new FakeTimeProvider();
        var logs = new LogRecorder();
        using IHost host = await StartAsync(probe, clock, options => options.Interval = Interval, logs);

        await AdvanceUntilAsync(clock, () => probe.Passes >= 2);

        Job(host).ExecuteTask!.IsCompleted.Should().BeFalse("a failed pass never ends the loop");
        logs.Messages.Should().Contain(message => message.Contains("Periodic job probe failed a pass", StringComparison.Ordinal));
        logs.Exceptions.Should().ContainSingle(exception => exception.Message == "pass-sentinel");
    }

    [Fact]
    public async Task QuietFailureLogging_LogsOnlyTheExceptionType()
    {
        var probe = new JobProbe { FailFirstPass = true };
        var logs = new LogRecorder();
        using IHost host = await StartAsync(
            probe,
            new FakeTimeProvider(),
            options =>
            {
                options.Interval = Interval;
                options.LogExceptionDetails = false;
            },
            logs);

        await WaitUntilAsync(() => logs.Messages.Any(message => message.Contains("failed a pass", StringComparison.Ordinal)));

        logs.Messages.Should().Contain(message => message.Contains("(InvalidOperationException)", StringComparison.Ordinal));
        logs.Exceptions.Should().BeEmpty();
        logs.Messages.Should().NotContain(message => message.Contains("pass-sentinel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PassTimeout_CancelsAHungPassAndTheLoopContinues()
    {
        var probe = new JobProbe { HangFirstPass = true };
        var clock = new FakeTimeProvider();
        var logs = new LogRecorder();
        using IHost host = await StartAsync(
            probe,
            clock,
            options =>
            {
                options.Interval = Interval;
                options.PassTimeout = TimeSpan.FromSeconds(5);
            },
            logs);

        await WaitUntilAsync(() => probe.Started.Task.IsCompleted);
        await AdvanceUntilAsync(clock, () => probe.Passes >= 2);

        probe.CanceledPasses.Should().Be(1);
        logs.Messages.Should().Contain(message => message.Contains("ran past its", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shutdown_CancelsThePassInFlightAndEndsQuietly()
    {
        var probe = new JobProbe { HangFirstPass = true };
        var logs = new LogRecorder();
        IHost host = await StartAsync(probe, new FakeTimeProvider(), options => options.Interval = Interval, logs);
        await probe.Started.Task.WaitAsync(Patience);

        using var budget = new CancellationTokenSource(Patience);
        await host.StopAsync(budget.Token);
        host.Dispose();

        probe.CanceledPasses.Should().Be(1);
        logs.Messages.Should().NotContain(message => message.Contains("failed a pass", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FixedRate_StartsPassesOnTicks()
    {
        var probe = new JobProbe();
        var clock = new FakeTimeProvider();
        using IHost host = await StartAsync(
            probe,
            clock,
            options =>
            {
                options.Interval = Interval;
                options.Cadence = PeriodicJobCadence.FixedRate;
            });

        await AdvanceUntilAsync(clock, () => probe.Passes >= 3);
    }

    [Fact]
    public async Task InitialDelay_HoldsTheFirstPass()
    {
        var probe = new JobProbe();
        var clock = new FakeTimeProvider();
        using IHost host = await StartAsync(
            probe,
            clock,
            options =>
            {
                options.Interval = Interval;
                options.InitialDelay = TimeSpan.FromMinutes(5);
            });

        await Task.Delay(100);
        probe.Passes.Should().Be(0);
        await AdvanceUntilAsync(clock, () => probe.Passes == 1, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void SameName_IsRefusedAtComposition()
    {
        var services = new ServiceCollection();
        services.AddPeriodicJob("sweep", (_, _) => Task.CompletedTask);

        var act = () => services.AddPeriodicJob("sweep", (_, _) => Task.CompletedTask);

        act.Should().Throw<InvalidOperationException>().WithMessage("*sweep*");
    }

    [Fact]
    public async Task DistinctJobs_RunSideBySide()
    {
        var first = new ConcurrentBag<string>();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddPeriodicJob("a", (_, _) => Add(first, "a"), (options, _) => options.Interval = Interval);
        builder.Services.AddPeriodicJob("b", (_, _) => Add(first, "b"), (options, _) => options.Interval = Interval);
        using IHost host = builder.Build();
        await host.StartAsync();

        await WaitUntilAsync(() => first.Distinct().Count() == 2);
        host.Services.GetServices<IHostedService>().OfType<PeriodicJobBackgroundService>().Select(job => job.Name)
            .Should().BeEquivalentTo("a", "b");

        static Task Add(ConcurrentBag<string> bag, string value)
        {
            bag.Add(value);
            return Task.CompletedTask;
        }
    }

    private static async Task<IHost> StartAsync(
        JobProbe probe, TimeProvider clock, Action<PeriodicJobOptions> configure, LogRecorder? logs = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        if (logs is not null)
        {
            builder.Logging.ClearProviders().AddProvider(logs);
        }

        builder.Services.AddSingleton(probe);
        builder.Services.AddSingleton(clock);
        builder.Services.AddScoped<ProbeService>();
        builder.Services.AddPeriodicJob<ProbeService>("probe", (service, ct) => service.RunAsync(ct), configure);
        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static PeriodicJobBackgroundService Job(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<PeriodicJobBackgroundService>().Single();

    private static async Task AdvanceUntilAsync(FakeTimeProvider clock, Func<bool> condition, TimeSpan? step = null)
    {
        DateTime deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the job should reach the expected state");
            clock.Advance(step ?? Interval);
            await Task.Delay(20);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the job should reach the expected state");
            await Task.Delay(10);
        }
    }

    private sealed class JobProbe
    {
        private int _passes;
        private int _canceled;

        public ConcurrentQueue<Guid> Instances { get; } = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailFirstPass { get; init; }

        public bool HangFirstPass { get; init; }

        public int Passes => Volatile.Read(ref _passes);

        public int CanceledPasses => Volatile.Read(ref _canceled);

        public int NextPass() => Interlocked.Increment(ref _passes);

        public void Canceled() => Interlocked.Increment(ref _canceled);
    }

    private sealed class ProbeService(JobProbe probe)
    {
        private readonly Guid _instance = Guid.NewGuid();

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            probe.Instances.Enqueue(_instance);
            int pass = probe.NextPass();
            probe.Started.TrySetResult();
            if (pass == 1 && probe.FailFirstPass)
            {
                throw new InvalidOperationException("pass-sentinel");
            }

            if (pass == 1 && probe.HangFirstPass)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    probe.Canceled();
                    throw;
                }
            }
        }
    }

    private sealed class LogRecorder : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ConcurrentQueue<Exception> Exceptions { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Recorder(this);

        public void Dispose()
        {
        }

        private sealed class Recorder(LogRecorder owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Messages.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    owner.Exceptions.Enqueue(exception);
                }
            }
        }
    }
}
