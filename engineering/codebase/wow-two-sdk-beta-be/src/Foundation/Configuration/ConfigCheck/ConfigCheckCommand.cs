using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Answers <c>app config check</c> from an image's own services: the boot's settings validation, run without serving.</summary>
/// <remarks>
///   - reports invalid values (every recorded settings record, as the boot validates it), unknown keys under the service's own
///     sections (<see cref="ConfigCheckServiceCollectionExtensions.AddSettingsRoot"/>) and stranding (<see cref="IStrandingCheck"/>)
///   - starts no listener and no hosted service, and writes nothing
///   - exits <c>0</c> consistent · <c>1</c> refused · <c>2</c> failed, such as an unreachable database or a usage error;
///     the last stdout line is one <c>config-result {json}</c> line, the human report goes to stderr
///   - the service host dispatches it (<c>ServiceHost</c> in <c>Meta/</c>); call <see cref="RunAsync"/> directly for a host without one
/// </remarks>
public static class ConfigCheckCommand
{
    /// <summary>Holds the command's name: the first argument that selects it.</summary>
    public const string Name = "config";

    /// <summary>Holds the one verb: <c>check</c>.</summary>
    public const string CheckVerb = "check";

    /// <summary>Holds the usage line printed for any other arguments.</summary>
    public const string Usage = "usage: config check";

    /// <summary>Returns whether the process arguments ask for the config command: <c>config</c> first.</summary>
    /// <param name="args">The process arguments.</param>
    public static bool IsCommand(string[] args) => args is [Name, ..];

    /// <summary>Runs one invocation over a built host's services, writing the human report to <paramref name="error"/> and the result line last to <paramref name="output"/>.</summary>
    /// <param name="services">The built host's root provider; never started, never disposed.</param>
    /// <param name="service">The service name the result line carries, such as <c>identity</c>.</param>
    /// <param name="args">The arguments, <c>config</c> first: <c>config check</c>.</param>
    /// <param name="output">Standard output: the result line only.</param>
    /// <param name="error">Standard error: the human report.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The exit code: <c>0</c> consistent · <c>1</c> refused · <c>2</c> failed.</returns>
    public static async Task<int> RunAsync(
        IServiceProvider services, string service, IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ConfigCheckRun run;
        if (args is [Name, CheckVerb])
        {
            run = await ConfigCheckRunner.RunAsync(services, service, ct).ConfigureAwait(false);
        }
        else
        {
            var given = string.Join(' ', args);
            run = new ConfigCheckRun { Result = new ConfigCheckResult { Service = service, Error = Usage }, Notes = [$"unrecognized arguments: {given}"] };
        }

        WriteHuman(run, error);
        await output.WriteLineAsync(run.Result.ToResultLine()).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
        await error.FlushAsync(ct).ConfigureAwait(false);
        return run.Result.Exit;
    }

    private static void WriteHuman(ConfigCheckRun run, TextWriter error)
    {
        var result = run.Result;
        var (mark, word) = result.Exit switch
        {
            0 => ("✓", "consistent"),
            1 => ("✗", "refused"),
            _ => ("✗", "failed"),
        };
        error.WriteLine($"{mark} config check: {word} (exit {result.Exit.ToString(CultureInfo.InvariantCulture)})");
        if (result.Error is { } message)
            error.WriteLine($"  {message}");
        foreach (var invalid in result.Invalid ?? [])
            error.WriteLine($"  invalid  {invalid.Setting} [{invalid.Code}]: {invalid.Message}");
        foreach (var key in result.Unknown ?? [])
            error.WriteLine($"  unknown  {key}: bound by no settings record");
        foreach (var stranded in result.Stranded ?? [])
            error.WriteLine($"  stranded {stranded.Setting} [{stranded.Code}]: {stranded.Count.ToString(CultureInfo.InvariantCulture)} people left no way in");
        foreach (var note in run.Notes)
            error.WriteLine($"  note     {note}");
    }
}
