namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Holds the bounds and environment policy of one process client.</summary>
public sealed record ProcessClientOptions
{
    /// <summary>Gets or sets the default time limit of a run; past it, the process and its children are killed. Defaults to 60 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the variables a child inherits from this process; every other variable is withheld, so ambient
    /// settings and secrets never leak. Defaults to <see cref="ProcessEnvironmentConstants.Baseline"/>; which names to add stays the
    /// application's choice.</summary>
    public IList<string> InheritedVariables { get; set; } = [.. ProcessEnvironmentConstants.Baseline];

    /// <summary>Gets or sets the most standard output read, in bytes; past it, the process is killed. Defaults to 16 MiB.</summary>
    public long MaxOutputBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Gets or sets how much of the standard error's tail is kept, in bytes. Defaults to 64 KiB.</summary>
    public int MaxErrorBytes { get; set; } = 64 * 1024;

    /// <summary>Gets or sets the parser that turns a failed run's standard error into its error; <see langword="null"/> (the default)
    /// returns the generic error.</summary>
    public IProcessFailureParser? FailureParser { get; set; }

    /// <summary>Gets or sets the folder a process starts in when its spec names none; the current folder when <see langword="null"/>.</summary>
    public string? WorkingDirectory { get; set; }
}
