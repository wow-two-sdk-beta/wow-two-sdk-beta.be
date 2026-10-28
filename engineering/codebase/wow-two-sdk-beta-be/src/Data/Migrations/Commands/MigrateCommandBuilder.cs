using System.CommandLine;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Builds the migrate verb tree once, for every host: an image's <c>app migrate &lt;verb&gt;</c> and the <c>wow-migrate</c> tool.</summary>
/// <remarks>
///   - verbs: <c>check</c>, <c>plan</c>, <c>apply [--dry-run]</c>, <c>seed</c>, <c>sets</c>, <c>rollback --to N</c>, <c>forget --above N</c>, <c>repair</c>,
///     <c>baseline [--to N]</c>, <c>claim --owner o --instance i</c>, <c>locks</c>, <c>unlock</c>, and <c>bg …</c>, refused until background migrations ship
///   - <c>--set</c> and <c>--json</c> are recursive on the parent; every writing operator verb takes <c>--confirm &lt;database&gt;</c>
///   - the bodies live in <see cref="MigrateCommandRunner"/>; parse errors still end in a refused <c>migrate-result</c> line
/// </remarks>
internal sealed class MigrateCommandBuilder
{
    private readonly MigrateCommandRunner _runner;
    private readonly MigrateReportWriter _writer;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>Creates a builder whose verbs run over <paramref name="host"/> and write to the given streams.</summary>
    /// <param name="host">Where the tree runs.</param>
    /// <param name="output">Standard output: the JSON report and the result line.</param>
    /// <param name="error">Standard error: the human report.</param>
    public MigrateCommandBuilder(IMigrateCommandHost host, TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
        _writer = new MigrateReportWriter(output, error);
        _runner = new MigrateCommandRunner(host, _writer);
    }

    /// <summary>Gets the recursive <c>--set</c> option: one set by name; without it, every set.</summary>
    public Option<string?> Set { get; } = new("--set")
    {
        Description = "Run one set by name; without it every set. A writing operator verb needs it when the host has several sets.",
        Recursive = true,
    };

    /// <summary>Gets the recursive <c>--json</c> option: the verb's JSON report on stdout before the result line, instead of the human report on stderr.</summary>
    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Print the verb's JSON report on stdout before the migrate-result line, instead of the human report on stderr.",
        Recursive = true,
    };

    /// <summary>Adds the verbs, <c>--set</c> and <c>--json</c> under <paramref name="parent"/>.</summary>
    /// <param name="parent">The image's <c>migrate</c> command, or the tool's root.</param>
    /// <param name="bareMigrate">Whether the parent alone runs <c>apply</c> then <c>seed</c>, as an image's <c>migrate</c> does.</param>
    public void AddVerbs(Command parent, bool bareMigrate)
    {
        ArgumentNullException.ThrowIfNull(parent);

        parent.Options.Add(Set);
        parent.Options.Add(Json);
        foreach (var verb in BuildVerbs())
            parent.Subcommands.Add(verb);

        if (bareMigrate)
            parent.SetAction((parse, ct) => _runner.MigrateAsync(Invocation(MigrateVerbs.Migrate, parse), ct));
    }

    /// <summary>Invokes a parse of the tree: a parse error is refused with exit <c>1</c> and a result line; anything else runs its verb.</summary>
    /// <param name="parse">The parse result.</param>
    /// <param name="handleTermination">Whether SIGINT and SIGTERM cancel the run, as in a process of its own.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> InvokeAsync(ParseResult parse, bool handleTermination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);

        if (parse.Errors.Count > 0)
        {
            _writer.WriteParseErrors(parse.CommandResult.Command.Name, parse.Errors.Select(error => error.Message));
            return 1;
        }

        var configuration = new InvocationConfiguration { EnableDefaultExceptionHandler = false, Output = _output, Error = _error };
        if (!handleTermination)
            configuration.ProcessTerminationTimeout = null;

        return await parse.InvokeAsync(configuration, ct).ConfigureAwait(false);
    }

    private IEnumerable<Command> BuildVerbs()
    {
        yield return Verb(MigrateVerbs.Check, "Compare each set with this image, read-only: 0 when it may run (current, or a compatible ahead), else 1.",
            (invocation, _, ct) => _runner.CheckAsync(invocation, ct));
        yield return Verb(MigrateVerbs.Plan, "List each set's pending migrations with their kinds; takes no lock and writes nothing.",
            (invocation, _, ct) => _runner.PlanAsync(invocation, ct));

        var dryRun = new Option<bool>("--dry-run") { Description = "Rehearse the pending migrations in one transaction and roll back; never creates the database." };
        yield return Verb(MigrateVerbs.Apply, "Create the database when missing, then apply every set's pending migrations in dependency order.",
            (invocation, parse, ct) => _runner.ApplyAsync(invocation, parse.GetValue(dryRun), ct), dryRun);

        yield return Verb(MigrateVerbs.Seed, "Apply every set's seed files not yet journalled, after apply.",
            (invocation, _, ct) => _runner.SeedAsync(invocation, ct));
        yield return Verb(MigrateVerbs.Sets, "Describe every set this image carries — level, contracts, dependencies, extensions, package — without settings or a database.",
            (invocation, _, ct) => _runner.SetsAsync(invocation, ct));

        var rollbackTo = new Option<int>("--to") { Description = "The level to roll back to.", Required = true };
        var rollbackConfirm = Confirm();
        yield return Verb(MigrateVerbs.Rollback, "Run Rollback.sql newest first down to --to N and delete their rows; one set, confirmed.",
            (invocation, parse, ct) => _runner.RollbackAsync(invocation, parse.GetValue(rollbackTo), parse.GetValue(rollbackConfirm), ct),
            rollbackTo, rollbackConfirm);

        var above = new Option<int>("--above") { Description = "The level to keep; rows above it are forgotten.", Required = true };
        var includeContracts = new Option<bool>("--include-contracts") { Description = "Forget a contract or a row with no kind too." };
        var forgetConfirm = Confirm();
        yield return Verb(MigrateVerbs.Forget, "Delete history rows above --above N without running SQL; one set, confirmed.",
            (invocation, parse, ct) => _runner.ForgetAsync(invocation, parse.GetValue(above), parse.GetValue(includeContracts), parse.GetValue(forgetConfirm), ct),
            above, includeContracts, forgetConfirm);

        var repairConfirm = Confirm();
        yield return Verb(MigrateVerbs.Repair, "Re-record drifted checksums; one set, confirmed.",
            (invocation, parse, ct) => _runner.RepairAsync(invocation, parse.GetValue(repairConfirm), ct), repairConfirm);

        var baselineTo = new Option<int?>("--to") { Description = "The last ordinal to record as applied; every source migration without it." };
        var baselineConfirm = Confirm();
        yield return Verb(MigrateVerbs.Baseline, "Record migrations as applied without running them, so an existing database adopts the set; one set, confirmed.",
            (invocation, parse, ct) => _runner.BaselineAsync(invocation, parse.GetValue(baselineTo), parse.GetValue(baselineConfirm), ct),
            baselineTo, baselineConfirm);

        var owner = new Option<string>("--owner") { Description = "The owner kind, such as module or service.", Required = true };
        var instance = new Option<string>("--instance") { Description = "The instance, such as identity-haven@prod.", Required = true };
        var claimConfirm = Confirm();
        yield return Verb(MigrateVerbs.Claim, "Record the set's schema marker under an owner and instance, replacing any marker; one set, confirmed.",
            (invocation, parse, ct) => _runner.ClaimAsync(invocation, parse.GetValue(owner)!, parse.GetValue(instance)!, parse.GetValue(claimConfirm), ct),
            owner, instance, claimConfirm);

        yield return Verb(MigrateVerbs.Locks, "List the sessions holding each set's migrator locks, read-only.",
            (invocation, _, ct) => _runner.LocksAsync(invocation, ct));

        var unlockConfirm = Confirm();
        yield return Verb(MigrateVerbs.Unlock, "End every session holding the set's migrator locks; one set, confirmed.",
            (invocation, parse, ct) => _runner.UnlockAsync(invocation, parse.GetValue(unlockConfirm), ct), unlockConfirm);

        var background = new Command(MigrateVerbs.Background, "Control background migrations — pause, resume, retry, rollback; refused until they ship.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        background.SetAction(parse => _runner.Background(Invocation(MigrateVerbs.Background, parse)));
        yield return background;
    }

    private Command Verb(string name, string description, Func<MigrateInvocation, ParseResult, CancellationToken, Task<int>> action, params Option[] options)
    {
        var command = new Command(name, description);
        foreach (var option in options)
            command.Options.Add(option);
        command.SetAction((parse, ct) => action(Invocation(name, parse), parse, ct));
        return command;
    }

    private static Option<string?> Confirm() => new("--confirm")
    {
        Description = "The name of the database this verb writes to; it must match the database the set connects to.",
    };

    private MigrateInvocation Invocation(string verb, ParseResult parse) => new(verb, parse.GetValue(Set), parse.GetValue(Json), parse);
}
