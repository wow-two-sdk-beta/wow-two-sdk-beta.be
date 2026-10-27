using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data;

/// <summary>Boot and deploy steps for hosts registered via <see cref="PostgresPersistenceServiceCollectionExtensions.AddPostgresPersistence{TContext}"/>: apply at boot on a local run, verify at boot elsewhere, apply in the deploy step.</summary>
/// <remarks>
///   - production migrates in an explicit deploy step before the new image serves: <see cref="MigrateBespokeForDeployAsync"/> or <c>wow-migrate apply</c>
///   - a local run is <c>Development</c> without <c>DEPLOY_ENVIRONMENT</c>, so a deployed dev host verifies like production
/// </remarks>
public static partial class PostgresPersistenceStartupExtensions
{
    private const string LoggerCategory = "WoW.Two.Sdk.Beta.Be.Data.Migrations.Startup";

    /// <summary>The host stamp recorded on rows applied at boot.</summary>
    private const string StartupAppliedBy = "startup";

    /// <summary>The host stamp recorded on rows the deploy step applies.</summary>
    private const string DeployAppliedBy = "deploy";

    /// <summary>Applies every set's pending migrations on a local run, or verifies their levels read-only on any other boot.</summary>
    /// <remarks>See <see cref="MigrateBespokeOnStartupAsync(IServiceProvider, Action{StartupMigrationOptions}?, CancellationToken)"/>.</remarks>
    /// <param name="services">The root service provider.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">A deployed host's database does not match this build.</exception>
    public static ValueTask MigrateBespokeOnStartupAsync(this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.MigrateBespokeOnStartupAsync(configure: null, cancellationToken);

    /// <summary>Applies every set's pending migrations on a local run, or verifies their levels read-only on any other boot unless the host opts in.</summary>
    /// <remarks>
    ///   - local run: creates the database if missing, then applies every set in registration order, stamped <c>startup</c>
    ///   - any other boot, or a host with no <see cref="IHostEnvironment"/>: <see cref="MigrationSetServiceProviderExtensions.VerifyMigrationLevelsOnStartupAsync"/>, which writes nothing
    ///   - <see cref="StartupMigrationOptions.ApplyWhenDeployed"/> applies on a deployed host too, and logs a warning (event <c>3014</c>)
    /// </remarks>
    /// <param name="services">The root service provider.</param>
    /// <param name="configure">An optional hook to opt a deployed host into applying at boot.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">A deployed host's database does not match this build.</exception>
    /// <exception cref="AppException">A set failed to apply, such as on drift or orphaned history.</exception>
    /// <example>
    /// <code>
    /// await app.Services.MigrateBespokeOnStartupAsync(app.Lifetime.ApplicationStopping);
    /// </code>
    /// </example>
    public static async ValueTask MigrateBespokeOnStartupAsync(
        this IServiceProvider services, Action<StartupMigrationOptions>? configure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new StartupMigrationOptions();
        configure?.Invoke(options);
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;

        if (!LocalRunExtensions.IsLocalRun(services))
        {
            if (!options.ApplyWhenDeployed)
            {
                LogVerifyingOnly(logger, DescribeBoot(services));
                await services.VerifyMigrationLevelsOnStartupAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            LogApplyingByOptIn(logger, DescribeBoot(services));
        }

        // The host has no result channel before its pipeline exists, and it must not boot on a schema that failed to migrate.
        (await services.ApplyBespokeSetsAsync(StartupAppliedBy, cancellationToken).ConfigureAwait(false)).ValueOrThrow();
    }

    /// <summary>Creates the database if missing, then applies every set's pending migrations in registration order — the explicit migrate step a deploy runs before the new image serves.</summary>
    /// <remarks>
    ///   - for an image's own migrate command, such as <c>app migrate</c>; <c>wow-migrate apply --sets</c> does the same from outside
    ///   - an owned schema is claimed under this host's owner instance, so the image that boots afterwards matches the claim
    /// </remarks>
    /// <param name="services">The root service provider.</param>
    /// <param name="appliedBy">The host stamp recorded on each applied row.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Each set's applied labels, or the first set's failure prefixed with its name.</returns>
    /// <exception cref="System.Data.Common.DbException">A migration script failed; later sets are not attempted.</exception>
    /// <example>
    /// <code>
    /// if (args is ["migrate"])
    ///     return (await app.Services.MigrateBespokeForDeployAsync()).IsSuccess ? 0 : 1;
    /// </code>
    /// </example>
    public static Task<Result<IReadOnlyList<MigrationSetApplied>>> MigrateBespokeForDeployAsync(
        this IServiceProvider services, string appliedBy = DeployAppliedBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedBy);

        return services.ApplyBespokeSetsAsync(appliedBy, cancellationToken);
    }

    /// <summary>Creates each set's database if missing, then applies every registered set.</summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="appliedBy">The host stamp recorded on each applied row.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static async Task<Result<IReadOnlyList<MigrationSetApplied>>> ApplyBespokeSetsAsync(
        this IServiceProvider services, string appliedBy, CancellationToken cancellationToken)
    {
        await services.EnsureSetDatabasesExistAsync(cancellationToken).ConfigureAwait(false);
        return await services.ApplyMigrationSetsAsync(appliedBy, setNames: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates each set's database via its maintenance database before any migration runs, once per connection string.</summary>
    /// <remarks>
    ///   - a set's database is the <see cref="DatabaseSettings"/> keyed by its name, else the host's
    ///   - a set with neither creates nothing; its first connection then reaches the database as registered
    ///   - each set creates through its own dialect, so a host with named sets only needs no default-set dialect
    /// </remarks>
    /// <param name="services">The root service provider.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static async Task EnsureSetDatabasesExistAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        var sets = services.GetRequiredService<IMigrationSetRegistry>();
        var hostSettings = services.GetService<DatabaseSettings>();
        var ensured = new HashSet<string>(StringComparer.Ordinal);

        foreach (var set in sets.Names)
        {
            var settings = services.GetKeyedService<DatabaseSettings>(set) ?? hostSettings;
            if (settings is null || !ensured.Add(settings.ConnectionString))
                continue;

            await sets.GetDialect(set).EnsureDatabaseExistsAsync(settings.ConnectionString, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Names the boot for a log line: the .NET environment and the deploy environment.</summary>
    /// <param name="services">The root service provider.</param>
    private static string DescribeBoot(IServiceProvider services)
    {
        var environment = services.GetService<IHostEnvironment>()?.EnvironmentName ?? "no host environment";
        var deployed = services.GetService<IConfiguration>()?[LocalRunExtensions.DeployEnvironmentVariable]
            ?? Environment.GetEnvironmentVariable(LocalRunExtensions.DeployEnvironmentVariable);
        return string.IsNullOrWhiteSpace(deployed) ? environment : $"{environment}, {LocalRunExtensions.DeployEnvironmentVariable}={deployed}";
    }

    [LoggerMessage(EventId = 3014, Level = LogLevel.Warning,
        Message = "Applying migrations at boot outside a local run ({Boot}) by explicit opt-in (StartupMigrationOptions.ApplyWhenDeployed); a deploy normally applies them in its migrate step.")]
    private static partial void LogApplyingByOptIn(ILogger logger, string boot);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Information,
        Message = "Boot outside a local run ({Boot}): verifying migration levels read-only; the deploy's migrate step applies them.")]
    private static partial void LogVerifyingOnly(ILogger logger, string boot);
}
