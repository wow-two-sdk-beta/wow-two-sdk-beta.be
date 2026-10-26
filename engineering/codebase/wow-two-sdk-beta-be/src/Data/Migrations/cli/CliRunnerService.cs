using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Provides the migrator command bodies — resolves connection and sets, wires the runners via DI, and prints results.</summary>
/// <remarks>
///   - connection resolves flag → <c>WOW_MIGRATE_DB_CONNECTION</c> → localhost default
///   - sets resolve <c>--sets</c> → <c>WOW_MIGRATE_SETS</c> → one set from <c>--sql-dir</c> → <c>WOW_MIGRATE_SQL_DIR</c> → auto-discovered default
///   - one default set prints exactly as before; any other run prints a <c>[set]</c> header per set
/// </remarks>
internal sealed partial class CliRunnerService(IMigrationsPathBroker pathBroker)
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    /// <summary>Resolves the connection string from the flag, the environment, then the localhost default.</summary>
    /// <param name="connectionFlag">The value of the global <c>--connection</c> option, or null when absent.</param>
    public string ResolveConnection(string? connectionFlag) =>
        connectionFlag
        ?? Environment.GetEnvironmentVariable("WOW_MIGRATE_DB_CONNECTION")
        ?? DefaultConnection;

    /// <summary>Resolves the migrations directory from the flag, the environment, then the auto-discovered default.</summary>
    /// <param name="sqlDirFlag">The value of the global <c>--sql-dir</c> option, or null when absent.</param>
    /// <remarks>Throws when auto-discovery finds no migrations folder — pass <c>--sql-dir</c>.</remarks>
    public string ResolveSqlDir(string? sqlDirFlag) =>
        sqlDirFlag
        ?? Environment.GetEnvironmentVariable("WOW_MIGRATE_SQL_DIR")
        ?? pathBroker.Locate().ValueOrThrow();

    /// <summary>Resolves the sets a verb runs: every manifest set, the one <c>--set</c> picks, or one set from the migrations directory.</summary>
    /// <param name="target">The global options.</param>
    /// <returns>The sets in run order, or a <c>Validation</c>, <c>NotFound</c> or <c>FileNotFound</c> failure.</returns>
    public Result<IReadOnlyList<CliMigrationSet>> ResolveSets(CliTarget target)
    {
        if (target.Set is { } picked && !CliMigrationSet.IsValidName(picked))
        {
            return Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.Validation(
                $"--set '{picked}' must be 'default' or a lowercase identifier (a-z first, then a-z, 0-9 and _)."));
        }

        var setsFile = target.SetsFile ?? Environment.GetEnvironmentVariable("WOW_MIGRATE_SETS");
        if (setsFile is null)
        {
            return Result<IReadOnlyList<CliMigrationSet>>.Ok([new CliMigrationSet(
                target.Set ?? MigrationConstants.DefaultSetName, ResolveSqlDir(target.SqlDir), null,
                CliMigrationSet.DefaultResourcePrefix, target.Schema, target.SchemaOwner, null)]);
        }

        if (target.SqlDir is not null || target.Schema is not null || target.SchemaOwner is not null)
        {
            return Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.Validation(
                "--sets defines every set; drop --sql-dir, --schema and --schema-owner, or drop --sets."));
        }

        if (CliMigrationSetManifest.Read(setsFile).IsFailure(out var manifestError, out var sets))
            return Result<IReadOnlyList<CliMigrationSet>>.Fail(manifestError);

        if (target.Set is null)
            return Result<IReadOnlyList<CliMigrationSet>>.Ok(sets);

        var selected = sets.Where(set => set.Name == target.Set).ToList();
        return selected.Count > 0
            ? Result<IReadOnlyList<CliMigrationSet>>.Ok(selected)
            : Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.NotFound(
                $"The manifest has no set '{target.Set}'; it lists {string.Join(", ", sets.Select(set => set.Name))}."));
    }

    /// <summary>Prints the applied, pending, drifted, and orphaned migrations of each set.</summary>
    /// <param name="target">The global options.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> StatusAsync(CliTarget target, CancellationToken ct)
    {
        if (!TryResolveSets(target, out var sets))
            return 1;

        await using var provider = BuildProvider(target.Connection, sets);
        var registry = provider.GetRequiredService<IMigrationSetRegistry>();
        var exitCode = 0;
        foreach (var set in sets)
        {
            WriteHeader(sets, set);
            var statusResult = await registry.GetRunner(set.Name).GetStatusAsync(ct);
            if (statusResult.IsFailure(out var statusError, out var status))
            {
                Console.Error.WriteLine($"✗ {statusError.Message}");
                exitCode = 1;
                continue;
            }

            if (status.SchemaOwner is { } owner)
                Console.WriteLine($"Owner: schema {owner.SchemaName} held by set {owner.SetName} as {owner.Owner} [{owner.ClaimedBy} @ {owner.ClaimedAt}]");

            Console.WriteLine($"Applied ({status.Applied.Count}):");
            foreach (var row in status.Applied)
                Console.WriteLine($"  ✓ {row.Ordinal:D3}-{row.Name}   [{row.AppliedBy} @ {row.AppliedAt:u}, {row.ExecutionMs}ms]");

            Console.WriteLine($"Pending ({status.Pending.Count}):");
            foreach (var migration in status.Pending)
                Console.WriteLine($"  • {migration.Label}");

            if (status.Drifted.Count > 0)
            {
                Console.WriteLine($"Drift ({status.Drifted.Count}):");
                foreach (var migration in status.Drifted)
                    Console.WriteLine($"  ! {migration.Label}");
            }

            if (status.Orphaned.Count > 0)
                Console.WriteLine($"Orphaned (in DB, not in source): {string.Join(", ", status.Orphaned.Select(o => o.ToString("D3", CultureInfo.InvariantCulture)))}");
        }

        return exitCode;
    }

    /// <summary>Ensures the database exists, then applies every pending migration of each set in order — the production deploy step.</summary>
    /// <remarks>Stops at the first set that fails; later sets are not attempted.</remarks>
    /// <param name="target">The global options.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> ApplyAsync(CliTarget target, CancellationToken ct)
    {
        if (!TryResolveSets(target, out var sets))
            return 1;

        await using var provider = BuildProvider(target.Connection, sets);
        var registry = provider.GetRequiredService<IMigrationSetRegistry>();

        // Create the target database first — a fresh box may have no target database yet.
        await registry.GetDialect(sets[0].Name).EnsureDatabaseExistsAsync(ResolveConnection(target.Connection), ct);
        foreach (var set in sets)
        {
            WriteHeader(sets, set);
            var applyResult = await registry.GetRunner(set.Name).ApplyPendingAsync("cli", ct);
            if (applyResult.IsFailure(out var applyError, out var applied))
            {
                Console.Error.WriteLine($"✗ {applyError.Message}");
                return 1;
            }

            Console.WriteLine(applied.Count == 0
                ? "✓ Up to date — nothing to apply."
                : $"✓ Applied {applied.Count}: {string.Join(", ", applied)}");
        }

        return 0;
    }

    /// <summary>Rehearses every pending migration of each set inside one rolled-back transaction per set and prints the reports.</summary>
    /// <remarks>
    ///   - exit 0 when every rehearsed migration succeeds, 1 on drift, orphans, an owner conflict or a malformed source, 2 when a migration fails
    ///   - never creates the database: a missing database fails the connection with exit 2
    /// </remarks>
    /// <param name="target">The global options.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> DryRunAsync(CliTarget target, CancellationToken ct)
    {
        if (!TryResolveSets(target, out var sets))
            return 1;

        await using var provider = BuildProvider(target.Connection, sets);
        var registry = provider.GetRequiredService<IMigrationSetRegistry>();
        var exitCode = 0;
        foreach (var set in sets)
        {
            WriteHeader(sets, set);
            exitCode = Math.Max(exitCode, await DryRunSetAsync(registry.GetRunner(set.Name), ct));
        }

        return exitCode;
    }

    /// <summary>Compares each set's applied level with its source without writing anything — the pre-start or readiness check.</summary>
    /// <remarks>Exit 0 when every set is current; 1 when one is behind, ahead, orphaned, drifted, claimed by another owner, or fails its check.</remarks>
    /// <param name="target">The global options.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> CheckAsync(CliTarget target, CancellationToken ct)
    {
        if (!TryResolveSets(target, out var sets))
            return 1;

        await using var provider = BuildProvider(target.Connection, sets);
        var levels = await provider.CheckMigrationLevelsAsync(sets.Select(set => set.Name).ToList(), ct);
        if (levels.IsFailure(out var levelError, out var reports))
        {
            Console.Error.WriteLine($"✗ {levelError.Message}");
            return 1;
        }

        var exitCode = 0;
        foreach (var report in reports)
        {
            if (report.IsCurrent)
            {
                Console.WriteLine($"✓ {report.Describe()}");
                continue;
            }

            Console.Error.WriteLine($"✗ {report.Describe()}");
            exitCode = 1;
        }

        return exitCode;
    }

    /// <summary>Rolls back the latest migration of one set, or every migration above the given ordinal (dev only, target-guarded).</summary>
    /// <param name="target">The global options; they must resolve to one set.</param>
    /// <param name="targetOrdinal">The ordinal to roll back to, or null to roll back only the latest.</param>
    /// <param name="confirmDb">The operator's claimed target database name, from <c>--i-understand-this-is</c>.</param>
    /// <param name="force">Whether to skip the interactive confirmation prompt.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> RollbackAsync(CliTarget target, int? targetOrdinal, string? confirmDb, bool force, CancellationToken ct)
    {
        if (!TryResolveSingleSet(target, "rollback", out var set))
            return 1;

        if (!ConfirmDestructiveTarget(target.Connection, confirmDb, force))
            return 2;

        await using var provider = BuildProvider(target.Connection, [set], allowRollback: true);
        var runner = provider.GetRequiredService<IMigrationSetRegistry>().GetRunner(set.Name);
        var rollbackResult = await runner.RollbackAsync(targetOrdinal, ct);
        if (rollbackResult.IsFailure(out var rollbackError, out var rolledBack))
        {
            Console.Error.WriteLine($"✗ {rollbackError.Message}");
            return 1;
        }

        Console.WriteLine(rolledBack.Count == 0
            ? "Nothing to roll back."
            : $"✓ Rolled back {rolledBack.Count}: {string.Join(", ", rolledBack)}");
        return 0;
    }

    /// <summary>Reports checksum drift of each set (exit 1 on any), or re-records one set's edited checksums when repair is requested (repair is target-guarded).</summary>
    /// <param name="target">The global options; with <paramref name="repair"/> they must resolve to one set.</param>
    /// <param name="repair">Whether to re-record drifted checksums instead of failing on them.</param>
    /// <param name="confirmDb">The operator's claimed target database name, from <c>--i-understand-this-is</c> (required only with <paramref name="repair"/>).</param>
    /// <param name="force">Whether to skip the interactive confirmation prompt when repairing.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> VerifyAsync(CliTarget target, bool repair, string? confirmDb, bool force, CancellationToken ct)
    {
        if (repair)
            return await RepairAsync(target, confirmDb, force, ct);

        if (!TryResolveSets(target, out var sets))
            return 1;

        await using var provider = BuildProvider(target.Connection, sets);
        var registry = provider.GetRequiredService<IMigrationSetRegistry>();
        var exitCode = 0;
        foreach (var set in sets)
        {
            WriteHeader(sets, set);
            if ((await registry.GetRunner(set.Name).GetStatusAsync(ct)).IsFailure(out var verifyError, out var status))
            {
                Console.Error.WriteLine($"✗ {verifyError.Message}");
                exitCode = 1;
                continue;
            }

            if (status.Drifted.Count == 0)
            {
                Console.WriteLine("✓ No drift — every applied migration matches its source.");
                continue;
            }

            await Console.Error.WriteLineAsync(
                $"✗ Drift in {status.Drifted.Count}: {string.Join(", ", status.Drifted.Select(m => m.Label))}. " +
                "Revert the edits, or run 'verify --repair' to accept them.");
            exitCode = 1;
        }

        return exitCode;
    }

    /// <summary>Scaffolds a new <c>Dev/&lt;name&gt;.sql</c> draft under the selected set's migrations directory.</summary>
    /// <param name="target">The global options; they must resolve to one directory set.</param>
    /// <param name="name">The draft name, slugified before use.</param>
    public int NewMigration(CliTarget target, string name)
    {
        if (!TryResolveAuthoringDir(target, out var root))
            return 1;

        var slug = Slugify(name);
        var devDir = Path.Combine(root, MigrationConstants.DevFolderName);
        Directory.CreateDirectory(devDir);

        var path = Path.Combine(devDir, $"{slug}.sql");
        if (File.Exists(path))
        {
            Console.Error.WriteLine($"✗ {path} already exists.");
            return 1;
        }

        File.WriteAllText(path,
            $"-- Dev migration: {slug}\n" +
            "-- Iterate freely. Promote to a numbered Apply/Rollback pair with: wow-migrate promote\n" +
            $"-- Put '{MigrationConstants.NoTransactionDirective}' as the FIRST line if this must run outside a transaction\n" +
            "--   (such files re-run on a mid-apply crash, so make them idempotent: IF NOT EXISTS, guarded DO blocks).\n\n");

        Console.WriteLine($"✓ Created {path}");
        return 0;
    }

    /// <summary>Promotes every <c>Dev/*.sql</c> draft of the selected set into a numbered <c>NNN-name/{Apply,Rollback}.sql</c> migration.</summary>
    /// <param name="target">The global options; they must resolve to one directory set.</param>
    public int PromoteDev(CliTarget target)
    {
        if (!TryResolveAuthoringDir(target, out var root))
            return 1;

        var devDir = Path.Combine(root, MigrationConstants.DevFolderName);
        if (!Directory.Exists(devDir))
        {
            Console.WriteLine("No Dev/ folder — nothing to promote.");
            return 0;
        }

        var devFiles = Directory.GetFiles(devDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (devFiles.Count == 0)
        {
            Console.WriteLine("No Dev/*.sql — nothing to promote.");
            return 0;
        }

        var next = NextOrdinal(root);
        foreach (var file in devFiles)
        {
            var name = Slugify(Path.GetFileNameWithoutExtension(file));
            var folder = Path.Combine(root, $"{next:D3}-{name}");
            Directory.CreateDirectory(folder);

            File.Copy(file, Path.Combine(folder, MigrationConstants.ApplyFileName));
            File.WriteAllText(Path.Combine(folder, MigrationConstants.RollbackFileName),
                $"-- Rollback for {next:D3}-{name}. Write the inverse of Apply.sql (dev/test only).\n");
            File.Delete(file);

            Console.WriteLine($"✓ Promoted Dev/{Path.GetFileName(file)} → {next:D3}-{name}/");
            next++;
        }

        return 0;
    }

    /// <summary>Re-records one set's drifted checksums after the destructive-target guard passes.</summary>
    /// <param name="target">The global options; they must resolve to one set.</param>
    /// <param name="confirmDb">The operator's claimed target database name.</param>
    /// <param name="force">Whether to skip the interactive confirmation prompt.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<int> RepairAsync(CliTarget target, string? confirmDb, bool force, CancellationToken ct)
    {
        if (!TryResolveSingleSet(target, "verify --repair", out var set))
            return 1;

        // Plain verify is read-only; only --repair mutates migration history, so guard only that path.
        if (!ConfirmDestructiveTarget(target.Connection, confirmDb, force))
            return 2;

        await using var provider = BuildProvider(target.Connection, [set], allowRollback: true);
        var runner = provider.GetRequiredService<IMigrationSetRegistry>().GetRunner(set.Name);
        if ((await runner.RepairAsync(ct)).IsFailure(out var repairError, out var repaired))
        {
            Console.Error.WriteLine($"✗ {repairError.Message}");
            return 1;
        }

        Console.WriteLine(repaired.Count == 0
            ? "✓ No drift to repair."
            : $"✓ Repaired checksums: {string.Join(", ", repaired)}");
        return 0;
    }

    /// <summary>Rehearses one set and prints its report.</summary>
    /// <param name="runner">The set's runner.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private static async Task<int> DryRunSetAsync(IMigrationRunnerService runner, CancellationToken ct)
    {
        var dryRunResult = await runner.DryRunPendingAsync(ct);
        if (dryRunResult.IsFailure(out var dryRunError, out var report))
        {
            Console.Error.WriteLine($"✗ {dryRunError.Message}");
            return 1;
        }

        if (report.WouldApply.Count == 0)
        {
            Console.WriteLine("✓ Up to date — nothing to rehearse.");
            return 0;
        }

        Console.WriteLine($"Dry run ({report.WouldApply.Count} pending, rolled back):");
        foreach (var label in report.WouldApply)
            Console.WriteLine($"  {DescribeRehearsal(report, label)}");

        if (report.Failure is { } failure)
        {
            if (failure.FollowsNotRehearsed)
                Console.Error.WriteLine($"  Note: {failure.Migration} follows a no-transaction script the dry run skipped and may depend on it.");
            Console.Error.WriteLine($"✗ Dry run failed at {failure.Migration}.");
            return 2;
        }

        Console.WriteLine(report.NotRehearsed.Count == 0
            ? $"✓ Dry run passed: {report.Succeeded.Count} rehearsed."
            : $"✓ Dry run passed: {report.Succeeded.Count} rehearsed, {report.NotRehearsed.Count} not rehearsed (no-transaction).");
        return 0;
    }

    /// <summary>Resolves the sets, printing the failure when they do not resolve.</summary>
    /// <param name="target">The global options.</param>
    /// <param name="sets">The resolved sets.</param>
    private bool TryResolveSets(CliTarget target, out IReadOnlyList<CliMigrationSet> sets)
    {
        if (ResolveSets(target).IsFailure(out var error, out var resolved))
        {
            Console.Error.WriteLine($"✗ {error.Message}");
            sets = [];
            return false;
        }

        sets = resolved;
        return true;
    }

    /// <summary>Resolves exactly one set for a verb that changes one history, printing why when it cannot.</summary>
    /// <param name="target">The global options.</param>
    /// <param name="verb">The verb, for the message.</param>
    /// <param name="set">The one resolved set.</param>
    private bool TryResolveSingleSet(CliTarget target, string verb, out CliMigrationSet set)
    {
        set = null!;
        if (!TryResolveSets(target, out var sets))
            return false;

        if (sets.Count != 1)
        {
            Console.Error.WriteLine($"✗ {verb} runs one set; pass --set <name> (sets: {string.Join(", ", sets.Select(s => s.Name))}).");
            return false;
        }

        set = sets[0];
        return true;
    }

    /// <summary>Resolves the migrations directory the authoring verbs write to: <c>--sql-dir</c>, or the one manifest set that reads a directory.</summary>
    /// <param name="target">The global options.</param>
    /// <param name="root">The resolved directory.</param>
    private bool TryResolveAuthoringDir(CliTarget target, out string root)
    {
        root = "";
        if (target.SetsFile is null && Environment.GetEnvironmentVariable("WOW_MIGRATE_SETS") is null)
        {
            root = ResolveSqlDir(target.SqlDir);
            return true;
        }

        if (!TryResolveSingleSet(target, "authoring", out var set))
            return false;

        if (set.SqlDir is null)
        {
            Console.Error.WriteLine($"✗ Set '{set.Name}' reads an assembly; author its migrations in the assembly's source tree.");
            return false;
        }

        root = set.SqlDir;
        return true;
    }

    /// <summary>Builds the DI provider that registers the data source, connection factory, and one migrator per set.</summary>
    /// <param name="connection">The global connection flag, resolved against env and default.</param>
    /// <param name="sets">The sets to register, in run order.</param>
    /// <param name="allowRollback">Whether to enable the engine's destructive ops (rollback / repair) — only the guarded destructive verbs pass true.</param>
    private ServiceProvider BuildProvider(string? connection, IReadOnlyList<CliMigrationSet> sets, bool allowRollback = false)
    {
        var dataSource = new NpgsqlDataSourceBuilder(ResolveConnection(connection)).Build();

        var services = new ServiceCollection()
            .AddSingleton<DbDataSource>(dataSource)
            .AddDataSourceConnectionFactory()
            // Engine errors (which migrations a failed batch rolled back) reach stderr; the verbs print the rest.
            .AddLogging(logging => logging.AddProvider(new StandardErrorLoggerProvider(LogLevel.Error)));

        foreach (var set in sets)
        {
            var broker = CreateBrokerFactory(set);
            void Configure(MigrationOptions options)
            {
                options.AllowRollback = allowRollback;
                if (set.Schema is { } schema)
                    options.SchemaName = schema;
                if (set.SchemaOwner is { } owner)
                    options.SchemaOwner = owner;
                if (set.HistoryTable is { } table)
                    options.TableName = table;
            }

            if (set.Name == MigrationConstants.DefaultSetName)
                services.AddDatabaseBespokeMigrations(broker, Configure);
            else
                services.AddDatabaseBespokeMigrationSet(set.Name, broker, Configure);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Creates the source of one set: its directory, or the embedded resources of its assembly.</summary>
    /// <param name="set">The set to read.</param>
    private static Func<IServiceProvider, IMigrationBroker> CreateBrokerFactory(CliMigrationSet set)
    {
        if (set.AssemblyPath is not { } assemblyPath)
            return _ => new FileSystemMigrationBroker(set.SqlDir!);

        return _ => new EmbeddedResourceMigrationBroker(LoadAssembly(assemblyPath), set.ResourcePrefix);
    }

    /// <summary>Loads an assembly into its own load context for its embedded resources only; its types are never used.</summary>
    /// <param name="assemblyPath">The assembly's full path.</param>
    /// <exception cref="FileNotFoundException">The assembly does not exist.</exception>
    private static Assembly LoadAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Migration-set assembly '{assemblyPath}' does not exist.", assemblyPath);

        return new AssemblyLoadContext($"wow-migrate:{Path.GetFileName(assemblyPath)}", isCollectible: true)
            .LoadFromAssemblyPath(assemblyPath);
    }

    /// <summary>Prints a set's header when the run is not the plain default set, so single-set output stays as before.</summary>
    /// <param name="sets">Every set of the run.</param>
    /// <param name="set">The set about to print.</param>
    private static void WriteHeader(IReadOnlyList<CliMigrationSet> sets, CliMigrationSet set)
    {
        if (sets.Count > 1 || set.Name != MigrationConstants.DefaultSetName)
            Console.WriteLine($"[{set.Name}]");
    }

    /// <summary>Confirms a destructive op against the resolved target database — the operator must name it, then confirm unless forced.</summary>
    /// <param name="connection">The global connection flag, resolved to derive the target database name.</param>
    /// <param name="confirmDb">The operator's claimed database name, from <c>--i-understand-this-is</c>.</param>
    /// <param name="force">Whether to skip the interactive <c>[y/N]</c> prompt.</param>
    private bool ConfirmDestructiveTarget(string? connection, string? confirmDb, bool force)
    {
        var target = new NpgsqlConnectionStringBuilder(ResolveConnection(connection)).Database ?? "(unknown)";

        if (!string.Equals(confirmDb, target, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"✗ Destructive op refused. Re-run with --i-understand-this-is {target} to confirm the target database.");
            return false;
        }

        if (force)
            return true;

        Console.Write($"This will modify database '{target}'. Continue? [y/N] ");
        if (Console.ReadLine()?.Trim() is "y" or "Y" or "yes" or "YES")
            return true;

        Console.Error.WriteLine("✗ Aborted.");
        return false;
    }

    /// <summary>Describes one pending migration's rehearsal outcome as a status line.</summary>
    /// <param name="report">The dry-run report.</param>
    /// <param name="label">The migration label to describe.</param>
    private static string DescribeRehearsal(MigrationDryRunReport report, string label)
    {
        if (report.Succeeded.Contains(label))
            return $"✓ {label}";
        if (report.NotRehearsed.Contains(label))
            return $"~ {label}  (no-transaction: not rehearsed)";
        if (report.Failure is { } failure && failure.Migration == label)
            return $"✗ {label}  {failure.Error}";
        return $"· {label}  (not reached)";
    }

    /// <summary>Computes the next ordinal — one past the highest <c>NNN-</c> prefix already on disk.</summary>
    /// <param name="root">The migrations directory to scan.</param>
    private static int NextOrdinal(string root)
    {
        var max = 0;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var match = OrdinalPrefix().Match(Path.GetFileName(dir));
            if (match.Success)
                max = Math.Max(max, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        return max + 1;
    }

    /// <summary>Lowercases and hyphenates a name into a filesystem-safe slug.</summary>
    /// <param name="value">The raw name to slugify.</param>
    private static string Slugify(string value) =>
        new string(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    /// <summary>Matches the leading three-digit ordinal of a numbered migration folder.</summary>
    [GeneratedRegex(@"^(\d{3})-")]
    private static partial Regex OrdinalPrefix();
}
