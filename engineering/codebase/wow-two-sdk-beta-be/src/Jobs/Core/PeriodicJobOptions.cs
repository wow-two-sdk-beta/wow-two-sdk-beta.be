namespace WoW.Two.Sdk.Beta.Be.Jobs.Core;

/// <summary>Holds options for one periodic job: how often a pass runs, how passes are spaced and how a failure is logged.</summary>
/// <remarks>Named after the job and resolved when the host starts, so configuration overrides applied after registration — a test host's, a local run's — still count.</remarks>
public sealed record PeriodicJobOptions
{
    /// <summary>Gets or sets the time between passes. Zero or less turns the job off. Defaults to one minute.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets how passes are spaced. Defaults to <see cref="PeriodicJobCadence.FixedDelay"/>.</summary>
    public PeriodicJobCadence Cadence { get; set; } = PeriodicJobCadence.FixedDelay;

    /// <summary>Gets or sets how long the first pass waits after the host starts. Defaults to zero: the first pass runs at once.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets the longest one pass may run before its token is cancelled; <see langword="null"/> (the default) sets no limit.</summary>
    public TimeSpan? PassTimeout { get; set; }

    /// <summary>Gets or sets whether a failed pass logs the exception itself. <see langword="false"/> logs only its type name, for work whose
    /// exceptions may carry inputs — process arguments, hosts, values. Defaults to <see langword="true"/>.</summary>
    public bool LogExceptionDetails { get; set; } = true;
}
