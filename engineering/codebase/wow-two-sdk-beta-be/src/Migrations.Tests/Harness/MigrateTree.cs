using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

/// <summary>Runs the image's migrate tree in process over a host's services and checks the result-line contract on every run.</summary>
public static class MigrateTree
{
    /// <summary>Runs <c>migrate</c> with <paramref name="args"/> and asserts the last stdout line is the parsed result, matching the exit.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="args">The arguments after <c>migrate</c>.</param>
    public static async Task<TreeRun> RunAsync(IServiceProvider services, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await MigrateCommand.RunAsync(services, [MigrateCommand.Name, .. args], output, error);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Should().NotBeEmpty(error.ToString());
        lines[^1].Should().StartWith(MigrateRunResult.ResultLinePrefix);
        var result = MigrateRunResult.FromOutput(output.ToString());
        result.Should().NotBeNull(lines[^1]);
        result!.Exit.Should().Be(exit, lines[^1]);
        return new TreeRun(exit, output.ToString(), error.ToString(), result);
    }
}

/// <summary>The outcome of one in-process tree run.</summary>
/// <param name="Exit">The exit code.</param>
/// <param name="Output">Everything written to stdout.</param>
/// <param name="Error">Everything written to stderr.</param>
/// <param name="Result">The parsed result line.</param>
public sealed record TreeRun(int Exit, string Output, string Error, MigrateRunResult Result)
{
    /// <summary>Gets the only set's entry.</summary>
    public MigrateSetResult Set => Result.Sets.Single();

    /// <inheritdoc />
    public override string ToString() => Output + Error;
}
