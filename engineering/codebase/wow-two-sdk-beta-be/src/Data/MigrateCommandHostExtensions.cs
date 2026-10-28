using Microsoft.Extensions.Hosting;

namespace WoW.Two.Sdk.Beta.Be.Data;

/// <summary>Runs <c>app migrate &lt;verb&gt;</c> from <c>Program.cs</c>, after <c>Build()</c> and before <c>Configure</c>, instead of starting the host.</summary>
public static class MigrateCommandHostExtensions
{
    /// <summary>Runs the migrate command over the built host's services and returns its exit code; the host never starts.</summary>
    /// <remarks>
    ///   - call it after <c>Build()</c> and before any startup work, so a migrate run starts no server, hosted service or warm-up
    ///   - stdout carries only the JSON report (<c>--json</c>) and the <c>migrate-result</c> line; console output stays on stderr for the rest of the process,
    ///     so a late log line never follows the result line
    ///   - SIGINT and SIGTERM cancel the run
    /// </remarks>
    /// <param name="host">The built host, such as a <c>WebApplication</c>.</param>
    /// <param name="args">The process arguments, <c>migrate</c> first.</param>
    /// <returns>The exit code: <c>0</c> done or current · <c>1</c> refused · <c>2</c> failed · <c>3</c> busy.</returns>
    /// <example>
    /// <code>
    /// var app = builder.Build();
    /// if (MigrateCommand.IsCommand(args))
    ///     return await app.RunMigrateCommandAsync(args);
    /// app.Configure();
    /// </code>
    /// </example>
    public static Task<int> RunMigrateCommandAsync(this IHost host, string[] args)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(args);

        // The host's own loggers write to Console.Out; point it at stderr so the result line stays the last stdout line.
        var output = Console.Out;
        Console.SetOut(Console.Error);
        return MigrateCommand.RunAsync(host.Services, args, output, Console.Error, handleTermination: true, CancellationToken.None);
    }
}
