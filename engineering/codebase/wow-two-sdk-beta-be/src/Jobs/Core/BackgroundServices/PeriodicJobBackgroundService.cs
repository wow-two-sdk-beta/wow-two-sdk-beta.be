using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.BackgroundServices;

/// <summary>Runs one periodic job on its configured interval, each pass in a fresh DI scope.</summary>
/// <remarks>
///   - the options are read when the host starts; an interval of zero or less leaves the job off
///   - a failed pass is logged and counted, and the next pass runs on schedule
///   - a pass that outlives its <see cref="PeriodicJobOptions.PassTimeout"/> is cancelled and counted as timed out
///   - shutdown cancels the pass in flight and ends the loop without an error
/// </remarks>
public sealed class PeriodicJobBackgroundService : BackgroundService
{
    private static readonly Meter Meter = new("WoW.Two.Sdk.Beta.Be.Jobs.Core");
    private static readonly Counter<long> Passes = Meter.CreateCounter<long>("periodic_job.passes");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("periodic_job.duration", "s");

    private readonly string _name;
    private readonly Func<IServiceProvider, CancellationToken, Task> _run;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<PeriodicJobOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PeriodicJobBackgroundService> _logger;

    /// <summary>Creates the loop for one named job.</summary>
    /// <param name="name">The job's name; it names the options and tags logs and metrics.</param>
    /// <param name="run">The pass, given the pass scope's services.</param>
    /// <param name="scopes">The factory that scopes each pass.</param>
    /// <param name="options">The named options, read when the loop starts.</param>
    /// <param name="clock">The clock that times the interval.</param>
    /// <param name="logger">The logger for failed and timed-out passes.</param>
    public PeriodicJobBackgroundService(
        string name,
        Func<IServiceProvider, CancellationToken, Task> run,
        IServiceScopeFactory scopes,
        IOptionsMonitor<PeriodicJobOptions> options,
        TimeProvider clock,
        ILogger<PeriodicJobBackgroundService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _name = name;
        _run = run;
        _scopes = scopes;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Gets the job's name.</summary>
    public string Name => _name;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PeriodicJobOptions options = _options.Get(_name);
        if (options.Interval <= TimeSpan.Zero)
        {
            _logger.PeriodicJobOff(_name);
            return;
        }

        try
        {
            if (options.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.InitialDelay, _clock, stoppingToken).ConfigureAwait(false);
            }

            if (options.Cadence == PeriodicJobCadence.FixedRate)
            {
                using var timer = new PeriodicTimer(options.Interval, _clock);
                do
                {
                    await RunPassAsync(options, stoppingToken).ConfigureAwait(false);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
            }
            else
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await RunPassAsync(options, stoppingToken).ConfigureAwait(false);
                    await Task.Delay(options.Interval, _clock, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown ends the loop; the pass in flight saw the same token.
        }
    }

    private async Task RunPassAsync(PeriodicJobOptions options, CancellationToken stoppingToken)
    {
        using CancellationTokenSource? deadline = options.PassTimeout is { } timeout && timeout > TimeSpan.Zero
            ? new CancellationTokenSource(timeout, _clock)
            : null;
        using CancellationTokenSource pass = deadline is null
            ? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
            : CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);

        long started = _clock.GetTimestamp();
        string outcome = "succeeded";
        try
        {
            AsyncServiceScope scope = _scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await _run(scope.ServiceProvider, pass.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            outcome = "canceled";
            throw;
        }
        catch (OperationCanceledException) when (deadline is { IsCancellationRequested: true })
        {
            outcome = "timed_out";
            _logger.PeriodicJobTimedOut(_name, options.PassTimeout!.Value);
        }
        catch (Exception exception)
        {
            outcome = "failed";
            if (options.LogExceptionDetails)
            {
                _logger.PeriodicJobFailed(exception, _name);
            }
            else
            {
                _logger.PeriodicJobFailedQuietly(_name, exception.GetType().Name);
            }
        }
        finally
        {
            var tags = new TagList { { "job", _name }, { "outcome", outcome } };
            Passes.Add(1, tags);
            Duration.Record(_clock.GetElapsedTime(started).TotalSeconds, tags);
        }
    }
}
