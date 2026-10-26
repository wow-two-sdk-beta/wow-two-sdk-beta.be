using System.CommandLine;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Builds the <c>wow-migrate</c> command tree — the global options and the status, apply, check, rollback, verify, new, and promote verbs.</summary>
/// <remarks>
///   - the global options are recursive — every verb accepts them
///   - <c>--sets</c> names a manifest of every set; <c>--set</c> picks one; without either the verb runs the default set as before
///   - <c>apply --dry-run</c> rehearses and rolls back, for a deploy runner to gate a release; <c>check</c> compares levels read-only
///   - <c>rollback</c> and <c>verify --repair</c> demand <c>--i-understand-this-is &lt;db&gt;</c> and run one set
/// </remarks>
internal sealed class CliCommandBuilder
{
    /// <summary>Builds the root command with the global options applied recursively to every subcommand.</summary>
    public RootCommand Build()
    {
        // One runner serves the whole command tree.
        var runner = new CliRunnerService(new MigrationsPathBroker());
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
            new Option<string?>("--set")
            {
                Description = "Run one named set: pick it from --sets, or name the set read from --sql-dir (else 'default').",
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
            });

        var root = new RootCommand("wow-migrate — web-free SQL migrator CLI.");
        foreach (var option in options.All)
            root.Options.Add(option);

        root.Subcommands.Add(BuildStatus(runner, options));
        root.Subcommands.Add(BuildApply(runner, options));
        root.Subcommands.Add(BuildCheck(runner, options));
        root.Subcommands.Add(BuildRollback(runner, options));
        root.Subcommands.Add(BuildVerify(runner, options));
        root.Subcommands.Add(BuildNew(runner, options));
        root.Subcommands.Add(BuildPromote(runner, options));

        return root;
    }

    /// <summary>Builds the <c>status</c> verb — show applied, pending, drift, and orphaned migrations per set.</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildStatus(CliRunnerService runner, GlobalOptions options)
    {
        var command = new Command("status", "Show applied / pending / drift / orphaned.");
        command.SetAction((parseResult, ct) => runner.StatusAsync(options.Read(parseResult), ct));
        return command;
    }

    /// <summary>Builds the <c>apply</c> verb — ensure the database exists, then apply pending migrations, or rehearse them with <c>--dry-run</c>.</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildApply(CliRunnerService runner, GlobalOptions options)
    {
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Rehearse pending migrations in one transaction and roll back; exit 2 when one fails. Never creates the DB.",
        };

        var command = new Command("apply", "Ensure the DB exists, then apply pending migrations (or rehearse them with --dry-run).")
        {
            dryRunOption,
        };
        command.SetAction((parseResult, ct) => parseResult.GetValue(dryRunOption)
            ? runner.DryRunAsync(options.Read(parseResult), ct)
            : runner.ApplyAsync(options.Read(parseResult), ct));
        return command;
    }

    /// <summary>Builds the <c>check</c> verb — compare each set's applied level with the source, read-only.</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildCheck(CliRunnerService runner, GlobalOptions options)
    {
        var command = new Command("check", "Exit 1 unless every set's database level matches its source; writes nothing.");
        command.SetAction((parseResult, ct) => runner.CheckAsync(options.Read(parseResult), ct));
        return command;
    }

    /// <summary>Builds the <c>rollback</c> verb — roll back the latest migration, or down to <c>--to N</c> (dev only).</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildRollback(CliRunnerService runner, GlobalOptions options)
    {
        var toOption = new Option<int?>("--to")
        {
            Description = "Roll back down to this ordinal (else just the latest).",
        };

        var confirmOption = new Option<string?>("--i-understand-this-is")
        {
            Description = "Confirm the target database name (must match the connection's database).",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Skip the interactive [y/N] confirmation prompt.",
        };

        var command = new Command("rollback", "Roll back the latest migration (or down to ordinal N) of one set. Dev only.")
        {
            toOption,
            confirmOption,
            forceOption,
        };
        command.SetAction((parseResult, ct) =>
            runner.RollbackAsync(
                options.Read(parseResult), parseResult.GetValue(toOption),
                parseResult.GetValue(confirmOption), parseResult.GetValue(forceOption), ct));
        return command;
    }

    /// <summary>Builds the <c>verify</c> verb — fail on checksum drift, or re-record it with <c>--repair</c>.</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildVerify(CliRunnerService runner, GlobalOptions options)
    {
        var repairOption = new Option<bool>("--repair")
        {
            Description = "Re-record edited checksums of one set instead of failing on drift.",
        };

        var confirmOption = new Option<string?>("--i-understand-this-is")
        {
            Description = "Confirm the target database name (required with --repair).",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Skip the interactive [y/N] confirmation prompt when repairing.",
        };

        var command = new Command("verify", "Exit 1 on checksum drift; --repair re-records edited checksums.")
        {
            repairOption,
            confirmOption,
            forceOption,
        };
        command.SetAction((parseResult, ct) =>
            runner.VerifyAsync(
                options.Read(parseResult), parseResult.GetValue(repairOption),
                parseResult.GetValue(confirmOption), parseResult.GetValue(forceOption), ct));
        return command;
    }

    /// <summary>Builds the <c>new</c> verb — scaffold a <c>Dev/&lt;name&gt;.sql</c> draft.</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildNew(CliRunnerService runner, GlobalOptions options)
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "The draft migration name (slugified).",
        };

        var command = new Command("new", "Scaffold Dev/<name>.sql.")
        {
            nameArgument,
        };
        command.SetAction((parseResult, _) =>
            Task.FromResult(runner.NewMigration(options.Read(parseResult), parseResult.GetValue(nameArgument)!)));
        return command;
    }

    /// <summary>Builds the <c>promote</c> verb — promote <c>Dev/*.sql</c> drafts into numbered migrations.</summary>
    /// <param name="runner">The runner carrying every command body.</param>
    /// <param name="options">The global options to read in the action.</param>
    private static Command BuildPromote(CliRunnerService runner, GlobalOptions options)
    {
        var command = new Command("promote", "Promote Dev/*.sql → numbered NNN-name/{Apply,Rollback}.sql.");
        command.SetAction((parseResult, _) =>
            Task.FromResult(runner.PromoteDev(options.Read(parseResult))));
        return command;
    }

    /// <summary>Holds the recursive global options and reads them into a <see cref="CliTarget"/>.</summary>
    /// <param name="Connection">The <c>--connection</c> option.</param>
    /// <param name="SqlDir">The <c>--sql-dir</c> option.</param>
    /// <param name="SetsFile">The <c>--sets</c> option.</param>
    /// <param name="Set">The <c>--set</c> option.</param>
    /// <param name="Schema">The <c>--schema</c> option.</param>
    /// <param name="SchemaOwner">The <c>--schema-owner</c> option.</param>
    private sealed record GlobalOptions(
        Option<string?> Connection, Option<string?> SqlDir, Option<string?> SetsFile,
        Option<string?> Set, Option<string?> Schema, Option<string?> SchemaOwner)
    {
        /// <summary>Gets every global option, in help order.</summary>
        public IEnumerable<Option> All => [Connection, SqlDir, SetsFile, Set, Schema, SchemaOwner];

        /// <summary>Reads the global options a verb was invoked with.</summary>
        /// <param name="parseResult">The parse result of the invocation.</param>
        public CliTarget Read(ParseResult parseResult) => new(
            parseResult.GetValue(Connection), parseResult.GetValue(SqlDir), parseResult.GetValue(SetsFile),
            parseResult.GetValue(Set), parseResult.GetValue(Schema), parseResult.GetValue(SchemaOwner));
    }
}
