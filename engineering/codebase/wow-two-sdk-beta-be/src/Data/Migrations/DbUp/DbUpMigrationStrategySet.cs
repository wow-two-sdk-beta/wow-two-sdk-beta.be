using System.Data.Common;
using System.Reflection;
using DbUp.Engine;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.DbUp;

/// <summary>Answers the migrate tree for a DbUp journal: <c>check</c>, <c>plan</c>, <c>apply</c> and <c>sets</c> through DbUp's upgrade engine.</summary>
/// <remarks>
///   - DbUp scripts carry no kinds, so a journalled script this image lacks counts as an incompatible ahead: <c>check</c> and <c>apply</c> refuse it
///   - the level is the number of journalled scripts; a label is the script's resource name
///   - <c>apply --dry-run</c> refuses: DbUp cannot rehearse a script and roll it back
///   - inside a migrate command, <c>Migrations:ConnectionString</c>, when set, replaces <see cref="DbUpOptions.ConnectionString"/>
/// </remarks>
/// <param name="options">The runner's options: engine factory, scripts and set name.</param>
/// <param name="runMode">The migrate command's switch, or null when the host registered none.</param>
/// <param name="configuration">The host configuration, which holds the migrate key.</param>
internal sealed class DbUpMigrationStrategySet(DbUpOptions options, MigrationRunMode? runMode, IConfiguration? configuration) : IMigrationStrategySet
{
    /// <summary>The strategy name a DbUp set reports.</summary>
    private const string DbUpStrategy = "dbup";

    /// <inheritdoc />
    public string Name => options.SetName;

    /// <inheritdoc />
    public string Strategy => DbUpStrategy;

    /// <inheritdoc />
    public string Schema => options.SchemaName;

    /// <inheritdoc />
    public Task<MigrateSetDescription> DescribeAsync(CancellationToken ct)
    {
        var assembly = ScriptsAssembly();
        return Task.FromResult(new MigrateSetDescription
        {
            Name = Name,
            Schema = Schema,
            Strategy = DbUpStrategy,
            Level = SourceScripts(assembly).Count,
            Package = assembly.GetName().Name,
            Version = MigrationRelease.TextOf(assembly),
        });
    }

    /// <inheritdoc />
    public Task<MigrateSetResult> CheckAsync(CancellationToken ct) => Task.FromResult(Read(read => read.Result with
    {
        Status = read.Pending.Count == 0 && read.Ahead.Count == 0 ? MigrateOutcome.Current : MigrateOutcome.Refused,
        Message = read.Ahead.Count > 0 ? AheadMessage(read.Ahead) : read.Pending.Count > 0 ? $"{read.Pending.Count} DbUp script(s) pending: the migrate step has not run." : null,
    }));

    /// <inheritdoc />
    public Task<MigrateSetResult> PlanAsync(CancellationToken ct) => Task.FromResult(Read(read => read.Result with
    {
        Status = MigrateOutcome.Current,
        Message = read.Ahead.Count > 0 ? AheadMessage(read.Ahead) : null,
    }));

    /// <inheritdoc />
    public Task<MigrateSetResult> ApplyAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
            return Task.FromResult(Base() with { Status = MigrateOutcome.Refused, Message = $"The dbup set '{Name}' cannot rehearse a dry run; plan lists what apply would run." });

        return Task.FromResult(Read(read =>
        {
            if (read.Ahead.Count > 0)
                return read.Result with { Status = MigrateOutcome.Refused, Message = AheadMessage(read.Ahead) };
            if (read.Pending.Count == 0)
                return read.Result with { Status = MigrateOutcome.Current };

            var upgrade = read.Engine.PerformUpgrade();
            var applied = upgrade.Scripts.Select(script => new MigrationLabel(script.Name, null)).ToList();
            if (!upgrade.Successful)
            {
                return read.Result with
                {
                    Status = MigrateOutcome.Failed,
                    Applied = applied,
                    Message = $"DbUp script {upgrade.ErrorScript?.Name ?? "(unknown)"} failed: {upgrade.Error?.Message}",
                    SqlState = (upgrade.Error as DbException)?.SqlState,
                };
            }

            return read.Result with
            {
                Status = MigrateOutcome.Done,
                State = MigrationLevelState.Current,
                LevelAfter = read.Result.LevelBefore + applied.Count,
                Applied = applied,
            };
        }));
    }

    /// <summary>Reads the journal and the image's scripts, then lets <paramref name="describe"/> decide; a provider error fails the set.</summary>
    private MigrateSetResult Read(Func<(MigrateSetResult Result, UpgradeEngine Engine, IReadOnlyList<string> Pending, IReadOnlyList<string> Ahead), MigrateSetResult> describe)
    {
        if (options.UpgradeEngineFactory is null)
            return Base() with { Status = MigrateOutcome.Refused, Message = "DbUpOptions.UpgradeEngineFactory is not set (call UsePostgres(), UseSqlServer() or UseMySql())." };

        try
        {
            var assembly = ScriptsAssembly();
            var builder = options.UpgradeEngineFactory(ConnectionString());
            builder = options.ScriptsNamespacePrefix is { } prefix
                ? builder.WithScriptsEmbeddedInAssembly(assembly, name => name.StartsWith(prefix, StringComparison.Ordinal))
                : builder.WithScriptsEmbeddedInAssembly(assembly);
            var engine = builder.LogToNowhere().Build();

            // The journal reads no table that does not exist, so a fresh database reads every script pending.
            var executed = engine.GetExecutedScripts();
            var source = SourceScripts(assembly);
            var pending = source.Except(executed, StringComparer.Ordinal).ToList();
            var ahead = executed.Except(source, StringComparer.Ordinal).ToList();
            var result = Base() with
            {
                State = ahead.Count > 0 ? MigrationLevelState.AheadIncompatible : pending.Count > 0 ? MigrationLevelState.Behind : MigrationLevelState.Current,
                LevelBefore = executed.Count,
                LevelAfter = executed.Count,
                Pending = pending.Select(name => new MigrationLabel(name, null)).ToList(),
                IncompatibleAhead = ahead,
            };
            return describe((result, engine, pending, ahead));
        }
        catch (DbException exception)
        {
            return Base() with { Status = MigrateOutcome.Failed, Message = exception.Message, SqlState = exception.SqlState };
        }
    }

    private string ConnectionString()
    {
        var key = runMode?.IsMigrateCommand == true
            ? configuration?[MigrationConstants.ConnectionStringConfigurationKey]
              ?? Environment.GetEnvironmentVariable(MigrationConstants.ConnectionStringConfigurationKey.Replace(":", "__", StringComparison.Ordinal))
            : null;
        return string.IsNullOrWhiteSpace(key) ? options.ConnectionString : key;
    }

    private Assembly ScriptsAssembly() => options.ScriptsAssembly ?? Assembly.GetEntryAssembly()
        ?? throw new InvalidOperationException("No DbUp scripts assembly resolved; set DbUpOptions.ScriptsAssembly.");

    /// <summary>Lists the embedded scripts as DbUp names them, without a connection: every <c>.sql</c> resource under the prefix.</summary>
    private List<string> SourceScripts(Assembly assembly) => assembly.GetManifestResourceNames()
        .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        .Where(name => options.ScriptsNamespacePrefix is not { } prefix || name.StartsWith(prefix, StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToList();

    private MigrateSetResult Base() => new() { Name = Name, Schema = Schema, Status = MigrateOutcome.Current };

    private static string AheadMessage(IReadOnlyList<string> ahead) =>
        $"The journal holds DbUp scripts this image lacks, which carry no kind and count as incompatible: {string.Join(", ", ahead)}.";
}
