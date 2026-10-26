namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Specifies why a child process run failed; <see cref="ProcessFailureMapper.KindOf"/> reads it off the error.</summary>
public enum ProcessFailureKind
{
    /// <summary>The process could not start: a missing executable, a refused permission, a missing working folder.</summary>
    NotStarted,

    /// <summary>The process ran past its time limit; it and its children were killed.</summary>
    TimedOut,

    /// <summary>The process exited with a code outside its success codes.</summary>
    Exited,

    /// <summary>The process wrote more standard output than allowed; it and its children were killed.</summary>
    OutputTooLarge,

    /// <summary>The process succeeded but its standard output is not the expected JSON.</summary>
    InvalidOutput,
}
