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
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Resolves the tool's connection and sets, builds the provider each migrate verb runs over, and carries the authoring verbs.</summary>
/// <remarks>
///   - connection resolves flag → <c>WOW_MIGRATE_DB_CONNECTION</c> → localhost default
///   - sets resolve <c>--sets</c> → <c>WOW_MIGRATE_SETS</c> → one set from <c>--sql-dir</c> → <c>WOW_MIGRATE_SQL_DIR</c> → auto-discovered default
///   - the migrate verbs' bodies live in the shared tree; <see cref="Open"/> hands it one provider per invocation
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
    /// <returns>The directory, or a <c>FileNotFound</c> failure when auto-discovery finds no migrations folder — pass <c>--sql-dir</c>.</returns>
    public Result<string> ResolveSqlDir(string? sqlDirFlag) =>
        (sqlDirFlag ?? Environment.GetEnvironmentVariable("WOW_MIGRATE_SQL_DIR")) is { } explicitDir
            ? Result<string>.Ok(explicitDir)
            : pathBroker.Locate();

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
            if (ResolveSqlDir(target.SqlDir).IsFailure(out var missing, out var sqlDir))
                return Result<IReadOnlyList<CliMigrationSet>>.Fail(missing);

            return RequireOwnerInstances([new CliMigrationSet(
                target.Set ?? MigrationConstants.DefaultSetName, sqlDir, null,
                CliMigrationSet.DefaultResourcePrefix, target.Schema, target.SchemaOwner, null, target.OwnerInstance)]);
        }

        if (target.SqlDir is not null || target.Schema is not null || target.SchemaOwner is not null || target.OwnerInstance is not null)
        {
            return Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.Validation(
                "--sets defines every set; drop --sql-dir, --schema, --schema-owner and --owner-instance, or drop --sets."));
        }

        if (CliMigrationSetManifest.Read(setsFile).IsFailure(out var manifestError, out var sets))
            return Result<IReadOnlyList<CliMigrationSet>>.Fail(manifestError);

        // Every set registers, so dependencies resolve; the tree's --set picks the sets a verb runs.
        return target.Set is null || sets.Any(set => set.Name == target.Set)
            ? RequireOwnerInstances(sets)
            : Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.NotFound(
                $"The manifest has no set '{target.Set}'; it lists {string.Join(", ", sets.Select(set => set.Name))}."));
    }

    /// <summary>Builds the provider one migrate verb runs over: the data source, the connection factory and one registration per set.</summary>
    /// <param name="connection">The global connection flag, resolved against the environment and the default.</param>
    /// <param name="sets">The sets to register, in run order.</param>
    /// <returns>The scope; disposing it disposes the provider and the data source.</returns>
    public IMigrateCommandScope Open(string? connection, IReadOnlyList<CliMigrationSet> sets)
    {
        var connectionString = ResolveConnection(connection);
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        return new CliMigrateCommandScope(BuildProvider(dataSource, sets), dataSource, connectionString);
    }

    /// <summary>Fails when a set claims its schema without naming the instance: the CLI is not the instance, so it cannot derive the host's value.</summary>
    /// <param name="sets">The resolved sets.</param>
    private static Result<IReadOnlyList<CliMigrationSet>> RequireOwnerInstances(IReadOnlyList<CliMigrationSet> sets)
    {
        var unnamed = sets.FirstOrDefault(set => set.SchemaOwner is not null && string.IsNullOrWhiteSpace(set.OwnerInstance));
        return unnamed is null
            ? Result<IReadOnlyList<CliMigrationSet>>.Ok(sets)
            : Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.Validation(
                $"Set '{unnamed.Name}' claims its schema as '{unnamed.SchemaOwner}' but names no instance; pass --owner-instance " +
                "(manifest: \"ownerInstance\") with the value the instance's own host claims under: its Migrations:OwnerInstance, " +
                "else {applicationName}@{DEPLOY_ENVIRONMENT}."));
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
        if (!TryResolveSets(target, out var resolved))
            return false;

        var sets = target.Set is null ? resolved : resolved.Where(candidate => candidate.Name == target.Set).ToList();
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
            if (ResolveSqlDir(target.SqlDir).IsFailure(out var missing, out var sqlDir))
            {
                Console.Error.WriteLine($"✗ {missing.Message}");
                return false;
            }

            root = sqlDir;
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

    /// <summary>Builds the DI provider that registers the data source, the connection factory, and one migrator per set.</summary>
    /// <param name="dataSource">The data source every set connects through; the caller disposes it.</param>
    /// <param name="sets">The sets to register, in run order.</param>
    private static ServiceProvider BuildProvider(NpgsqlDataSource dataSource, IReadOnlyList<CliMigrationSet> sets)
    {
        var services = new ServiceCollection()
            .AddSingleton<DbDataSource>(dataSource)
            .AddDataSourceConnectionFactory()
            // Engine errors (which migrations a failed batch rolled back) reach stderr; the tree prints the rest.
            .AddLogging(logging => logging.AddProvider(new StandardErrorLoggerProvider(LogLevel.Error)));

        foreach (var set in sets)
        {
            var broker = CreateBrokerFactory(set);
            void Configure(MigrationOptions options)
            {
                if (set.Schema is { } schema)
                    options.SchemaName = schema;
                if (set.SchemaOwner is { } owner)
                    options.SchemaOwner = owner;
                if (set.OwnerInstance is { } instance)
                    options.OwnerInstance = instance;
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
