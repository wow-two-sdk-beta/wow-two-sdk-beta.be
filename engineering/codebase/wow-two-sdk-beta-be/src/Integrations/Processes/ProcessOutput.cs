namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Represents a finished child process: its exit code and what it wrote.</summary>
public sealed record ProcessOutput
{
    /// <summary>Gets the exit code, one of the run's success codes.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Gets the standard output, decoded as UTF-8; bounded by <see cref="ProcessClientOptions.MaxOutputBytes"/>.</summary>
    public required string StandardOutput { get; init; }

    /// <summary>Gets the tail of the standard error, decoded as UTF-8; bounded by <see cref="ProcessClientOptions.MaxErrorBytes"/>.</summary>
    public required string StandardError { get; init; }

    /// <summary>Gets how long the process ran.</summary>
    public required TimeSpan Duration { get; init; }
}
