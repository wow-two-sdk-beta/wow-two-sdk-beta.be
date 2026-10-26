using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Runs child processes within bounds: an argument list, a scrubbed environment, a time limit that kills the process
/// tree, and capped output.</summary>
/// <remarks>A failure returns an error marked with its <see cref="ProcessFailureKind"/> (<see cref="ProcessFailureMapper.KindOf"/>);
/// its message never repeats arguments, environment values or output. Only the caller's cancellation throws.</remarks>
public interface IProcessClient
{
    /// <summary>Runs a process to its end.</summary>
    /// <param name="spec">The run.</param>
    /// <param name="cancellationToken">Cancels the run, killing the process tree.</param>
    /// <returns>The output when the exit code is a success code, else the failure.</returns>
    Task<Result<ProcessOutput>> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Runs a process to its end and parses its standard output as one JSON document.</summary>
    /// <param name="spec">The run.</param>
    /// <param name="cancellationToken">Cancels the run, killing the process tree.</param>
    /// <returns>The document's root, detached from the parser, or the failure.</returns>
    Task<Result<JsonElement>> RunJsonAsync(ProcessSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Starts a process that outlives the call: nothing waits for it, its output is read and dropped, and it is released when
    /// it exits. The spec's time limit does not apply.</summary>
    /// <param name="spec">The run; its standard input is written, then closed.</param>
    /// <returns>The process id, or the failure to start.</returns>
    Result<int> Start(ProcessSpec spec);
}
