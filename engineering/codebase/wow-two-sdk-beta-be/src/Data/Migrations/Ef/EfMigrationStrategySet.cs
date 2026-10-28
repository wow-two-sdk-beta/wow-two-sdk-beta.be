using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Ef;

/// <summary>Answers the migrate tree for an EF Core context: <c>check</c>, <c>plan</c>, <c>apply</c> and <c>sets</c> through <c>Database.MigrateAsync</c> and its history table.</summary>
/// <remarks>
///   - EF migrations carry no kinds, so history this image lacks counts as an incompatible ahead: <c>check</c> and <c>apply</c> refuse it
///   - the level is the number of applied migrations; a label is the migration id
///   - <c>apply --dry-run</c> refuses: EF cannot rehearse a migration and roll it back
///   - inside a migrate command, <c>Migrations:ConnectionString</c>, when set, replaces the context's connection string; a context built over a data source refuses it
/// </remarks>
/// <typeparam name="TContext">The migrated context.</typeparam>
/// <param name="scopes">Creates a scope per verb for the scoped context.</param>
/// <param name="options">The runner's options, which name the set.</param>
/// <param name="runMode">The migrate command's switch, or null when the host registered none.</param>
/// <param name="configuration">The host configuration, which holds the migrate key.</param>
internal sealed class EfMigrationStrategySet<TContext>(
    IServiceScopeFactory scopes, EfMigrationsOptions options, MigrationRunMode? runMode, IConfiguration? configuration) : IMigrationStrategySet
    where TContext : DbContext
{
    /// <summary>The strategy name an EF set reports.</summary>
    private const string EfStrategy = "ef";

    private string? _schema;

    /// <inheritdoc />
    public string Name => options.SetName;

    /// <inheritdoc />
    public string Strategy => EfStrategy;

    /// <inheritdoc />
    public string Schema => _schema ??= ReadSchema();

    /// <inheritdoc />
    public Task<MigrateSetDescription> DescribeAsync(CancellationToken ct)
    {
        var assembly = typeof(TContext).Assembly;
        return Task.FromResult(new MigrateSetDescription
        {
            Name = Name,
            Schema = Schema,
            Strategy = EfStrategy,
            Level = SourceMigrations().Count,
            Package = assembly.GetName().Name,
            Version = MigrationRelease.TextOf(assembly),
        });
    }

    /// <inheritdoc />
    public Task<MigrateSetResult> CheckAsync(CancellationToken ct) => ReadAsync((result, source, applied, pending, ahead) => result with
    {
        Status = pending.Count == 0 && ahead.Count == 0 ? MigrateOutcome.Current : MigrateOutcome.Refused,
        Message = ahead.Count > 0 ? AheadMessage(ahead) : pending.Count > 0 ? $"{pending.Count} EF migration(s) pending: the migrate step has not run." : null,
    }, ct);

    /// <inheritdoc />
    public Task<MigrateSetResult> PlanAsync(CancellationToken ct) => ReadAsync((result, _, _, _, ahead) => result with
    {
        Status = MigrateOutcome.Current,
        Message = ahead.Count > 0 ? AheadMessage(ahead) : null,
    }, ct);

    /// <inheritdoc />
    public async Task<MigrateSetResult> ApplyAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
            return Base() with { Status = MigrateOutcome.Refused, Message = $"The ef set '{Name}' cannot rehearse a dry run; plan lists what apply would run." };

        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            if (UseMigrateKey(context) is { } refused)
                return refused;

            var source = context.Database.GetMigrations().ToList();
            var before = (await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToList();
            var ahead = before.Except(source, StringComparer.Ordinal).ToList();
            var pending = source.Except(before, StringComparer.Ordinal).ToList();
            var result = Base() with { LevelBefore = before.Count, LevelAfter = before.Count, State = State(pending, ahead) };
            if (ahead.Count > 0)
                return result with { Status = MigrateOutcome.Refused, Message = AheadMessage(ahead) };
            if (pending.Count == 0)
                return result with { Status = MigrateOutcome.Current };

            await context.Database.MigrateAsync(ct).ConfigureAwait(false);
            var after = (await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).Count();
            return result with
            {
                Status = MigrateOutcome.Done,
                State = MigrationLevelState.Current,
                LevelAfter = after,
                Applied = pending.Select(id => new MigrationLabel(id, null)).ToList(),
            };
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            return Base() with
            {
                Status = exception is DbException ? MigrateOutcome.Failed : MigrateOutcome.Refused,
                Message = exception.Message,
                SqlState = (exception as DbException)?.SqlState,
            };
        }
    }

    private async Task<MigrateSetResult> ReadAsync(
        Func<MigrateSetResult, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, MigrateSetResult> describe,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            if (UseMigrateKey(context) is { } refused)
                return refused;

            // EF reads no history table that does not exist, so a fresh database reads every migration pending.
            var source = context.Database.GetMigrations().ToList();
            var applied = (await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToList();
            var pending = source.Except(applied, StringComparer.Ordinal).ToList();
            var ahead = applied.Except(source, StringComparer.Ordinal).ToList();
            return describe(Base() with
            {
                State = State(pending, ahead),
                LevelBefore = applied.Count,
                LevelAfter = applied.Count,
                Pending = pending.Select(id => new MigrationLabel(id, null)).ToList(),
                IncompatibleAhead = ahead,
            }, source, applied, pending, ahead);
        }
        catch (DbException exception)
        {
            return Base() with { Status = MigrateOutcome.Failed, Message = exception.Message, SqlState = exception.SqlState };
        }
    }

    /// <summary>Points the context at the migrate key inside a migrate command, when one is set.</summary>
    /// <returns>A refusal when the context cannot take a connection string, else null.</returns>
    private MigrateSetResult? UseMigrateKey(TContext context)
    {
        var key = runMode?.IsMigrateCommand == true ? MigrateKey() : null;
        if (key is null)
            return null;

        try
        {
            context.Database.SetConnectionString(key);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return Base() with
            {
                Status = MigrateOutcome.Refused,
                Message = $"The ef set '{Name}' cannot connect through Migrations:ConnectionString: {exception.Message}",
            };
        }
    }

    private string? MigrateKey()
    {
        var key = configuration?[MigrationConstants.ConnectionStringConfigurationKey]
            ?? Environment.GetEnvironmentVariable(MigrationConstants.ConnectionStringConfigurationKey.Replace(":", "__", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>Lists the image's migration ids: through the context, else from the context's assembly when no context resolves without settings.</summary>
    private List<string> SourceMigrations()
    {
        try
        {
            using var scope = scopes.CreateScope();
            return scope.ServiceProvider.GetRequiredService<TContext>().Database.GetMigrations().ToList();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return typeof(TContext).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(TContext))
                .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToList();
        }
    }

    private string ReadSchema()
    {
        try
        {
            using var scope = scopes.CreateScope();
            return scope.ServiceProvider.GetRequiredService<TContext>().Model.GetDefaultSchema() ?? "public";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return "public";
        }
    }

    private MigrateSetResult Base() => new() { Name = Name, Schema = Schema, Status = MigrateOutcome.Current };

    private static MigrationLevelState State(List<string> pending, List<string> ahead) =>
        ahead.Count > 0 ? MigrationLevelState.AheadIncompatible : pending.Count > 0 ? MigrationLevelState.Behind : MigrationLevelState.Current;

    private static string AheadMessage(IReadOnlyList<string> ahead) =>
        $"The history holds EF migrations this image lacks, which carry no kind and count as incompatible: {string.Join(", ", ahead)}.";
}
