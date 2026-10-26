namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Describes one child process run: the executable, its argument list, its input and its environment additions.</summary>
public sealed record ProcessSpec
{
    /// <summary>Gets the executable: a path, or a name the inherited <c>PATH</c> resolves.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the arguments, each passed as one value — an argument list, never a shell, so no value is re-parsed.
    /// Keep secrets out of them: a process listing shows every argument; pass them through <see cref="StandardInput"/>.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the folder the process starts in; the client's default when <see langword="null"/>.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets the text written to the process's standard input, which is then closed; <see langword="null"/> closes it at once.</summary>
    public string? StandardInput { get; init; }

    /// <summary>Gets variables set for this run on top of the inherited allowlist; a <see langword="null"/> value removes the variable.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>Gets this run's time limit; the client's <see cref="ProcessClientOptions.Timeout"/> when <see langword="null"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Gets the exit codes that count as success. Defaults to zero only.</summary>
    public IReadOnlyCollection<int> SuccessExitCodes { get; init; } = [0];
}
