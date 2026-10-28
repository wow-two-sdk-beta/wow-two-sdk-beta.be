using System.CommandLine;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Builds the <c>wow-migrate</c> command tree: the global options, the shared migrate verbs and the authoring verbs <c>new</c> and <c>promote</c>.</summary>
/// <remarks>
///   - the migrate verbs are the image's own tree (<see cref="MigrateCommandBuilder"/>): <c>check</c>, <c>plan</c>, <c>apply</c>, <c>seed</c>, <c>sets</c>,
///     <c>rollback</c>, <c>forget</c>, <c>repair</c>, <c>baseline</c>, <c>claim</c>, <c>locks</c>, <c>unlock</c>, <c>bg</c>, with the same exits and result line
///   - the global options are recursive; <c>--sets</c> names a manifest of every set, <c>--set</c> picks one; without either a verb runs the default set
///   - a set that claims its schema names the instance it claims as: <c>--owner-instance</c>, or the manifest's <c>ownerInstance</c>
/// </remarks>
/// <param name="output">Standard output: the JSON report and the result line.</param>
/// <param name="error">Standard error: the human report.</param>
internal sealed class CliCommandBuilder(TextWriter output, TextWriter error)
{
    /// <summary>Builds the root command and the migrate tree's builder, whose <see cref="MigrateCommandBuilder.InvokeAsync"/> runs a parse.</summary>
    public (RootCommand Root, MigrateCommandBuilder Tree) Build()
    {
        var options = new GlobalOptions(
            new Option<string?>("--connection")
            {
                Description = "Postgres connection (else WOW_MIGRATE_DB_CONNECTION, else localhost/postgres).",
                Recursive = true,
            },
            new Option<string?>("--sql-dir")
            {
                Description = "Migrations dir of a single set (else WOW_MIGRATE_SQL_DIR, else auto-discovered).",
                Recursive = true,
            },
            new Option<string?>("--sets")
            {
                Description = "Migration-sets manifest (JSON) listing every set (else WOW_MIGRATE_SETS); verbs run all of them unless --set picks one.",
                Recursive = true,
            },
            new Option<string?>("--schema")
            {
                Description = "Schema of a single set: holds its history and fills {{schema}} (else 'public').",
                Recursive = true,
            },
            new Option<string?>("--schema-owner")
            {
                Description = "Claim the single set's schema under this owner; creates the schema and refuses one claimed by another owner.",
                Recursive = true,
            },
            new Option<string?>("--owner-instance")
            {
                Description = "The instance the single set's schema is claimed as (required with --schema-owner): the host's Migrations:OwnerInstance, else {applicationName}@{DEPLOY_ENVIRONMENT}.",
                Recursive = true,
            });

        var runner = new CliRunnerService(new MigrationsPathBroker());
        var tree = new MigrateCommandBuilder(new CliMigrateCommandHost(runner, options.Read), output, error);
        var root = new RootCommand("wow-migrate — the migrate verb tree over a manifest of sets, plus the authoring verbs new and promote.");
        foreach (var option in options.All)
            root.Options.Add(option);
        tree.AddVerbs(root, bareMigrate: false);

        root.Subcommands.Add(BuildNew(runner, options, tree));
        root.Subcommands.Add(BuildPromote(runner, options, tree));
        return (root, tree);
    }

    /// <summary>Builds the <c>new</c> verb — scaffold a <c>Dev/&lt;name&gt;.sql</c> draft.</summary>
    /// <param name="runner">The runner carrying the authoring bodies.</param>
    /// <param name="options">The global options to read in the action.</param>
    /// <param name="tree">The migrate tree, whose <c>--set</c> picks the set to author.</param>
    private static Command BuildNew(CliRunnerService runner, GlobalOptions options, MigrateCommandBuilder tree)
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "The draft migration name (slugified).",
        };

        var command = new Command("new", "Scaffold Dev/<name>.sql.")
        {
            nameArgument,
        };
        command.SetAction(parseResult =>
            runner.NewMigration(options.Read(parseResult, parseResult.GetValue(tree.Set)), parseResult.GetValue(nameArgument)!));
        return command;
    }

    /// <summary>Builds the <c>promote</c> verb — promote <c>Dev/*.sql</c> drafts into numbered migrations.</summary>
    /// <param name="runner">The runner carrying the authoring bodies.</param>
    /// <param name="options">The global options to read in the action.</param>
    /// <param name="tree">The migrate tree, whose <c>--set</c> picks the set to author.</param>
    private static Command BuildPromote(CliRunnerService runner, GlobalOptions options, MigrateCommandBuilder tree)
    {
        var command = new Command("promote", "Promote Dev/*.sql → numbered NNN-name/{Apply,Rollback}.sql.");
        command.SetAction(parseResult => runner.PromoteDev(options.Read(parseResult, parseResult.GetValue(tree.Set))));
        return command;
    }

    /// <summary>Holds the recursive global options and reads them into a <see cref="CliTarget"/>.</summary>
    /// <param name="Connection">The <c>--connection</c> option.</param>
    /// <param name="SqlDir">The <c>--sql-dir</c> option.</param>
    /// <param name="SetsFile">The <c>--sets</c> option.</param>
    /// <param name="Schema">The <c>--schema</c> option.</param>
    /// <param name="SchemaOwner">The <c>--schema-owner</c> option.</param>
    /// <param name="OwnerInstance">The <c>--owner-instance</c> option.</param>
    private sealed record GlobalOptions(
        Option<string?> Connection, Option<string?> SqlDir, Option<string?> SetsFile,
        Option<string?> Schema, Option<string?> SchemaOwner, Option<string?> OwnerInstance)
    {
        /// <summary>Gets every global option, in help order.</summary>
        public IEnumerable<Option> All => [Connection, SqlDir, SetsFile, Schema, SchemaOwner, OwnerInstance];

        /// <summary>Reads the global options a verb was invoked with.</summary>
        /// <param name="parseResult">The parse result of the invocation.</param>
        /// <param name="set">The tree's <c>--set</c> value.</param>
        public CliTarget Read(ParseResult parseResult, string? set) => new(
            parseResult.GetValue(Connection), parseResult.GetValue(SqlDir), parseResult.GetValue(SetsFile),
            set, parseResult.GetValue(Schema), parseResult.GetValue(SchemaOwner), parseResult.GetValue(OwnerInstance));
    }
}
