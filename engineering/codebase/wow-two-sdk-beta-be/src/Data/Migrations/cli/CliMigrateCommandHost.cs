using System.CommandLine;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Runs the shared migrate tree over a provider the tool builds per invocation from its global options and manifest.</summary>
/// <param name="runner">Resolves the connection and the sets, and builds the provider.</param>
/// <param name="read">Reads the global options of a parse, with the tree's <c>--set</c>.</param>
internal sealed class CliMigrateCommandHost(CliRunnerService runner, Func<ParseResult, string?, CliTarget> read) : IMigrateCommandHost
{
    /// <inheritdoc />
    public Task<Result<IMigrateCommandScope>> OpenAsync(MigrateInvocation invocation, CancellationToken ct)
    {
        var target = read(invocation.Parse, invocation.Set);
        return Task.FromResult(runner.ResolveSets(target).IsFailure(out var error, out var sets)
            ? Result<IMigrateCommandScope>.Fail(error)
            : Result<IMigrateCommandScope>.Ok(runner.Open(target.Connection, sets)));
    }
}
