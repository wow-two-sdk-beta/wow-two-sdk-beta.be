using System.Diagnostics;
using AwesomeAssertions;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

/// <summary>Runs the built <c>wow-migrate</c> CLI as a separate process, the way a deploy runner calls it.</summary>
public static class CliProcess
{
    /// <summary>Runs the CLI with the given arguments and captures its exit code and output.</summary>
    /// <param name="arguments">The command-line arguments.</param>
    public static async Task<CliProcessResult> RunAsync(params string[] arguments)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = output.Name;
        var configuration = output.Parent!.Name;
        var sourceRoot = output.Parent.Parent!.Parent!.Parent!.FullName;
        var cli = Path.Combine(
            sourceRoot, "Data", "Migrations", "cli", "bin", configuration, targetFramework, "wow-migrate.dll");
        File.Exists(cli).Should().BeTrue($"the CLI project should build before tests: {cli}");

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(cli);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CliProcessResult(process.ExitCode, await standardOutput, await standardError);
    }
}

/// <summary>The outcome of one CLI process run.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
public sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);
