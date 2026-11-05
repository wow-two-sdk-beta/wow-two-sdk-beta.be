namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>Holds options for the person operation tracker.</summary>
public sealed record PersonOperationOptions
{
    /// <summary>
    /// Gets or sets how long a signal still applies to an operation that begins after it. Defaults to one hour; zero turns
    /// the memory off, so a signal reaches only the operations already running.
    /// </summary>
    public TimeSpan SignalMemory { get; set; } = TimeSpan.FromHours(1);
}
