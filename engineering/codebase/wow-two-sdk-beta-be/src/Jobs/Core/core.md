# Periodic jobs

> In-process work the host runs on an interval — samplers, followers, sweeps, retention loops. One registration
> replaces a hand-written `BackgroundService` loop.

```csharp
builder.Services.AddScoped<VitalsSampling>();
builder.Services.AddPeriodicJob<VitalsSampling>(
    "vitals",
    (sampling, ct) => sampling.SampleOnceAsync(ct),
    (options, services) =>
    {
        // Read when the host starts, so a test host's or local run's override still counts.
        var minutes = services.GetRequiredService<IConfiguration>().GetValue("Operations:VitalsSampleMinutes", 5);
        options.Interval = TimeSpan.FromMinutes(minutes);   // zero or less: the job is off
        options.PassTimeout = TimeSpan.FromMinutes(2);      // a hung pass is cancelled, the loop goes on
        options.LogExceptionDetails = false;                // log the exception type only
    });

// No service type: the pass gets its scope's services.
builder.Services.AddPeriodicJob("follow", (services, ct) => services.GetRequiredService<IGateway>().FollowAsync(ct),
    (options, _) => options.Interval = TimeSpan.FromSeconds(10));
```

- Each pass resolves its work from a fresh DI scope, so scoped services, `DbContext`s and the actor context start clean.
- The first pass runs at start, or after `InitialDelay`; `Cadence` picks `FixedDelay` (default) or `FixedRate`.
- A failed pass is logged and counted; the next pass runs on schedule. `LogExceptionDetails = false` logs the type only.
- `PassTimeout` cancels a pass that runs too long; it counts as `timed_out`, not as a failure of the loop.
- Shutdown cancels the pass in flight and ends the loop quietly. A non-shutdown cancellation is a failed pass.
- Options are named after the job and read at host start; a duplicate job name fails at composition.
- Delays use the registered `TimeProvider`, so tests drive passes with `FakeTimeProvider`.
- Metrics (`WoW.Two.Sdk.Beta.Be.Jobs.Core`): `periodic_job.passes` and `periodic_job.duration`, tagged `job` and
  `outcome` (`succeeded`, `failed`, `timed_out`, `canceled`).
- `options.UseConfiguredInterval(configuration, key, fallback, unit, min, max)` reads a whole number of units from
  configuration, clamps it and turns the job off at zero or less.
- A job whose pass starts a costly read — a child process, a remote call — asks a `WakeableSchedule` first: the read
  runs at once after `Wake()`, when the last read named its next look while work is open, else once per idle interval.
  `Settle` records a read, `Missed` a failed one; a wake during a read keeps the next pass due.
- One process runs each job. Work that needs one runner across replicas, retries or persistence belongs in
  [Hangfire](../Hangfire/Hangfire.md); queued items belong in [batch pipelines](../Batches/batches.md).
