using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Connects to the operating system's child processes: runs each one from an argument list with a scrubbed environment,
/// a time limit that kills the process tree, closed or supplied standard input, and capped output.</summary>
/// <remarks>
///   - the environment holds only <see cref="ProcessClientOptions.InheritedVariables"/> plus the spec's own variables
///   - a timeout, an output overflow or the caller's cancellation kills the process and its children
///   - a failed run is logged with the executable's name, the failure kind and the exit code — never arguments, values or output
/// </remarks>
public sealed partial class ProcessClient : IProcessClient
{
    private const int ChunkBytes = 16 * 1024;
    private static readonly Meter Meter = new("WoW.Two.Sdk.Beta.Be.Integrations.Processes");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("process.runs");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("process.duration", "s");
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan ReapBudget = TimeSpan.FromSeconds(5);

    private readonly ProcessClientOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProcessClient> _logger;

    /// <summary>Creates a client bounded by <paramref name="options"/>.</summary>
    /// <param name="options">The time limit, output caps, inherited variables and failure parser.</param>
    /// <param name="clock">The clock that times each run.</param>
    /// <param name="logger">The logger for failed runs.</param>
    public ProcessClient(ProcessClientOptions options, TimeProvider clock, ILogger<ProcessClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxOutputBytes, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxErrorBytes, 1, nameof(options));
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ProcessOutput>> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        Validate(spec);
        cancellationToken.ThrowIfCancellationRequested();
        string name = Path.GetFileName(spec.FileName);
        long started = _clock.GetTimestamp();

        using Process process = Create(spec);
        if (TryStart(process) is { } notStarted)
        {
            return Fail<ProcessOutput>(name, started, notStarted, ProcessFailureKind.NotStarted);
        }

        TimeSpan limit = spec.Timeout ?? _options.Timeout;
        using var deadline = new CancellationTokenSource(limit, _clock);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var state = new RunState();
        Task<byte[]> output = ReadBoundedAsync(process.StandardOutput.BaseStream, _options.MaxOutputBytes, state, abort);
        Task<byte[]> error = ReadTailAsync(process.StandardError.BaseStream, _options.MaxErrorBytes, abort.Token);
        try
        {
            await WriteInputAsync(process, spec.StandardInput, abort.Token).ConfigureAwait(false);
            byte[] standardOutput = await output.ConfigureAwait(false);
            byte[] standardError = await error.ConfigureAwait(false);
            await process.WaitForExitAsync(abort.Token).ConfigureAwait(false);
            if (state.Overflowed)
            {
                throw new OperationCanceledException(abort.Token);
            }

            int exitCode = process.ExitCode;
            string errorText = Utf8.GetString(standardError);
            if (!spec.SuccessExitCodes.Contains(exitCode))
            {
                AppError failure = _options.FailureParser?.Parse(exitCode, errorText)
                    ?? AppError.Of(AppErrorType.Unexpected, "The process failed.");
                return Fail<ProcessOutput>(name, started, failure, ProcessFailureKind.Exited, exitCode);
            }

            Record(name, started, "succeeded");
            return Result<ProcessOutput>.Ok(new ProcessOutput
            {
                ExitCode = exitCode,
                StandardOutput = Utf8.GetString(standardOutput),
                StandardError = errorText,
                Duration = _clock.GetElapsedTime(started),
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await KillAsync(process, output, error).ConfigureAwait(false);
            return state.Overflowed
                ? Fail<ProcessOutput>(name, started, AppError.Of(AppErrorType.Unexpected, "The process wrote more output than allowed."), ProcessFailureKind.OutputTooLarge)
                : Fail<ProcessOutput>(name, started, AppError.Of(AppErrorType.OperationTimeout, "The process did not finish in time."), ProcessFailureKind.TimedOut);
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process, output, error).ConfigureAwait(false);
            Record(name, started, "canceled");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<JsonElement>> RunJsonAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        long started = _clock.GetTimestamp();
        Result<ProcessOutput> run = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        if (run is Result<ProcessOutput>.Failure failure)
        {
            return Result<JsonElement>.Fail(failure.Error);
        }

        ProcessOutput output = ((Result<ProcessOutput>.Success)run).Value;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output.StandardOutput);
            return Result<JsonElement>.Ok(document.RootElement.Clone());
        }
        catch (JsonException)
        {
            AppError error = AppError.Of(AppErrorType.SerializationFailed, "The process output is not valid JSON.");
            return Fail<JsonElement>(Path.GetFileName(spec.FileName), started, error, ProcessFailureKind.InvalidOutput, output.ExitCode);
        }
    }

    /// <inheritdoc />
    public Result<int> Start(ProcessSpec spec)
    {
        Validate(spec);
        string name = Path.GetFileName(spec.FileName);
        long started = _clock.GetTimestamp();
        Process process = Create(spec);
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += static (_, _) => { };
        if (TryStart(process) is { } notStarted)
        {
            process.Dispose();
            return Fail<int>(name, started, notStarted, ProcessFailureKind.NotStarted);
        }

        int id = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Observe(WriteInputAsync(process, spec.StandardInput, CancellationToken.None));
        process.Exited += static (sender, _) => ((Process)sender!).Dispose();
        process.EnableRaisingEvents = true;
        Record(name, started, "started");
        LogStarted(_logger, name, id);
        return Result<int>.Ok(id);
    }

    private static void Validate(ProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.FileName, nameof(spec));
        if (spec.Timeout is { } timeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, nameof(spec));
        }
    }

    private Process Create(ProcessSpec spec)
    {
        var info = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = spec.WorkingDirectory ?? _options.WorkingDirectory ?? string.Empty,
        };
        foreach (string argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Nothing ambient passes: only the allowlist, then the spec's own values.
        info.Environment.Clear();
        foreach (string variable in _options.InheritedVariables)
        {
            if (System.Environment.GetEnvironmentVariable(variable) is { } value)
            {
                info.Environment[variable] = value;
            }
        }

        foreach ((string variable, string? value) in spec.Environment)
        {
            if (value is null)
            {
                info.Environment.Remove(variable);
            }
            else
            {
                info.Environment[variable] = value;
            }
        }

        return new Process { StartInfo = info };
    }

    private static AppError? TryStart(Process process)
    {
        try
        {
            process.Start();
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return AppError.Of(AppErrorType.ExternalUnavailable, "The process could not start.");
        }
    }

    private static async Task WriteInputAsync(Process process, string? input, CancellationToken cancellationToken)
    {
        try
        {
            if (input is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(Utf8.GetBytes(input), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // The process closed its input early; its exit code and output tell the outcome.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Same: the pipe is already gone.
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long limit, RunState state, CancellationTokenSource abort)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[ChunkBytes];
        int read;
        while ((read = await stream.ReadAsync(chunk, abort.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                state.Overflowed = true;
                await abort.CancelAsync().ConfigureAwait(false);
                return [];
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadTailAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[ChunkBytes];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > 2L * limit)
            {
                byte[] kept = buffer.ToArray()[^limit..];
                buffer.SetLength(0);
                buffer.Write(kept);
            }
        }

        byte[] all = buffer.ToArray();
        return all.Length > limit ? all[^limit..] : all;
    }

    private static async Task KillAsync(Process process, Task output, Task error)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using var reap = new CancellationTokenSource(ReapBudget);
            await process.WaitForExitAsync(reap.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            // The process exited between the check and the kill, or did not die within the budget.
        }

        Observe(output);
        Observe(error);
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private Result<T> Fail<T>(string name, long started, AppError error, ProcessFailureKind kind, int? exitCode = null)
        where T : notnull
    {
        TimeSpan elapsed = _clock.GetElapsedTime(started);
        LogFailed(_logger, name, kind, exitCode, elapsed);
        Record(name, started, kind.ToString());
        return Result<T>.Fail(ProcessFailureMapper.Mark(error, kind, exitCode));
    }

    private void Record(string name, long started, string outcome)
    {
        var tags = new TagList { { "process", name }, { "outcome", outcome } };
        Runs.Add(1, tags);
        Duration.Record(_clock.GetElapsedTime(started).TotalSeconds, tags);
    }

    [LoggerMessage(EventId = 8201, Level = LogLevel.Warning, Message = "Process {Process} failed ({Failure}, exit code {ExitCode}) after {Elapsed}")]
    private static partial void LogFailed(ILogger logger, string process, ProcessFailureKind failure, int? exitCode, TimeSpan elapsed);

    [LoggerMessage(EventId = 8202, Level = LogLevel.Debug, Message = "Process {Process} started detached as {ProcessId}")]
    private static partial void LogStarted(ILogger logger, string process, int processId);

    /// <summary>Holds what the output reader saw, so a cancellation can tell an overflow from a timeout.</summary>
    private sealed class RunState
    {
        private volatile bool _overflowed;

        public bool Overflowed
        {
            get => _overflowed;
            set => _overflowed = value;
        }
    }
}
