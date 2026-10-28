using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

/// <summary>Runs an image's <c>migrate</c> verbs in process over its services against a test database, and asserts the image contract (M57).</summary>
/// <remarks>
///   - every run checks the result line: the last stdout line starts <c>migrate-result </c>, parses, and carries the process exit
///   - <see cref="AssertRerunChangesNothingAsync"/> proves a verb idempotent; <see cref="AssertCheckWritesNothingAsync"/> proves <c>check</c> write-free
///   - build the services the way the image does (its host's registrations) over the test database; the harness never starts the host
/// </remarks>
/// <example>
/// <code>
/// var verbs = new MigrateVerbHarness(factory.Services);
/// await verbs.AssertCheckWritesNothingAsync();
/// await verbs.AssertExitAsync(0, "apply");
/// await verbs.AssertRerunChangesNothingAsync("apply");
/// </code>
/// </example>
public sealed class MigrateVerbHarness
{
    private readonly IServiceProvider _services;
    private readonly Func<CancellationToken, ValueTask<DbConnection>> _connect;

    /// <summary>Creates a harness over a host's services.</summary>
    /// <param name="services">The image's root provider, as its host registers it.</param>
    /// <param name="connect">Opens a connection to the test database for snapshots; null opens the host's <see cref="IDbConnectionFactory"/>.</param>
    public MigrateVerbHarness(IServiceProvider services, Func<CancellationToken, ValueTask<DbConnection>>? connect = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _connect = connect ?? (ct => services.GetRequiredService<IDbConnectionFactory>().CreateOpenAsync(ct));
    }

    /// <summary>Runs <c>migrate</c> with the given arguments, such as <c>apply --dry-run</c>, and checks the result line.</summary>
    /// <param name="args">The arguments after <c>migrate</c>.</param>
    /// <exception cref="MigrateVerbAssertionException">The result line is missing, unparsable or names another exit.</exception>
    public Task<MigrateVerbRun> RunAsync(params string[] args) => RunAsync(args, CancellationToken.None);

    /// <summary>Runs <c>migrate</c> with the given arguments and checks the result line.</summary>
    /// <param name="args">The arguments after <c>migrate</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="MigrateVerbAssertionException">The result line is missing, unparsable or names another exit.</exception>
    public async Task<MigrateVerbRun> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await MigrateCommand.RunAsync(_services, [MigrateCommand.Name, .. args], output, error, ct).ConfigureAwait(false);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = lines.Length == 0 ? "" : lines[^1];
        var result = last.StartsWith(MigrateRunResult.ResultLinePrefix, StringComparison.Ordinal)
            ? MigrateRunResult.FromJson(last[MigrateRunResult.ResultLinePrefix.Length..])
            : null;
        if (result is null)
            throw Fail($"migrate {string.Join(' ', args)}: the last stdout line is not a migrate-result line.", output, error);
        if (result.Exit != exit)
            throw Fail($"migrate {string.Join(' ', args)}: exited {exit} but the result line says {result.Exit}.", output, error);

        return new MigrateVerbRun(args, exit, output.ToString(), error.ToString(), result);
    }

    /// <summary>Runs a verb and asserts its exit.</summary>
    /// <param name="exit">The expected exit: <c>0</c> done or current · <c>1</c> refused · <c>2</c> failed · <c>3</c> busy.</param>
    /// <param name="args">The arguments after <c>migrate</c>.</param>
    /// <exception cref="MigrateVerbAssertionException">The exit differs, or the result line breaks the contract.</exception>
    public async Task<MigrateVerbRun> AssertExitAsync(int exit, params string[] args)
    {
        var run = await RunAsync(args).ConfigureAwait(false);
        return run.Exit == exit ? run : throw Fail($"expected exit {exit}: {run}");
    }

    /// <summary>Runs a verb, then reruns it, asserting the rerun exits <c>0</c>, reports no change and leaves the database as the first run left it.</summary>
    /// <param name="args">The arguments after <c>migrate</c>, such as <c>apply</c> or <c>forget --above 32 --confirm shop_test</c>.</param>
    /// <returns>The rerun.</returns>
    /// <exception cref="MigrateVerbAssertionException">The first run did not exit <c>0</c>, or the rerun changed something.</exception>
    public async Task<MigrateVerbRun> AssertRerunChangesNothingAsync(params string[] args)
    {
        await AssertExitAsync(0, args).ConfigureAwait(false);
        var before = await SnapshotAsync().ConfigureAwait(false);

        var rerun = await RunAsync(args).ConfigureAwait(false);

        var after = await SnapshotAsync().ConfigureAwait(false);
        if (rerun.Exit != 0 || !rerun.ChangedNothing)
            throw Fail($"a rerun must exit 0 and change nothing: {rerun}");
        return before == after ? rerun : throw Fail($"the rerun wrote to the database: {rerun}{Environment.NewLine}{Difference(before, after)}");
    }

    /// <summary>Runs <c>check</c> and asserts it wrote nothing: no schema, table, row, marker or lock appeared or changed.</summary>
    /// <param name="args">Further arguments, such as <c>--set identity</c>.</param>
    /// <returns>The run, which exits <c>0</c> when the image may run and <c>1</c> otherwise.</returns>
    /// <exception cref="MigrateVerbAssertionException">The check wrote, or exited neither <c>0</c> nor <c>1</c>.</exception>
    public async Task<MigrateVerbRun> AssertCheckWritesNothingAsync(params string[] args)
    {
        var before = await SnapshotAsync().ConfigureAwait(false);
        var run = await RunAsync([MigrateVerbs.Check, .. args], CancellationToken.None).ConfigureAwait(false);
        var after = await SnapshotAsync().ConfigureAwait(false);

        if (run.Exit is not (0 or 1))
            throw Fail($"check exits 0 or 1: {run}");
        return before == after ? run : throw Fail($"check wrote to the database: {run}{Environment.NewLine}{Difference(before, after)}");
    }

    private async Task<string> SnapshotAsync()
    {
        var connection = await _connect(CancellationToken.None).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
            return await DatabaseSnapshot.TakeAsync(connection, CancellationToken.None).ConfigureAwait(false);
    }

    private static string Difference(string before, string after)
    {
        var old = before.Split('\n').ToHashSet(StringComparer.Ordinal);
        var now = after.Split('\n').ToHashSet(StringComparer.Ordinal);
        return string.Join(Environment.NewLine, old.Except(now).Select(line => "- " + line).Concat(now.Except(old).Select(line => "+ " + line)));
    }

    private static MigrateVerbAssertionException Fail(string message, StringWriter? output = null, StringWriter? error = null) =>
        new(output is null ? message : $"{message}{Environment.NewLine}{output}{error}");
}
