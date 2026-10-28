using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Holds the provider one tool invocation runs over, and the data source it built; disposing the scope disposes both.</summary>
/// <param name="provider">The invocation's provider.</param>
/// <param name="dataSource">The data source every set connects through; the provider does not own a registered instance.</param>
/// <param name="connectionString">The resolved connection string, whose database <c>apply</c> creates when missing.</param>
internal sealed class CliMigrateCommandScope(ServiceProvider provider, NpgsqlDataSource dataSource, string connectionString) : IMigrateCommandScope
{
    /// <summary>Holds the stamp the tool records on rows <c>apply</c> and <c>seed</c> write.</summary>
    private const string CliAppliedBy = "cli";

    /// <inheritdoc />
    public IServiceProvider Services => provider;

    /// <inheritdoc />
    public string AppliedBy => CliAppliedBy;

    /// <inheritdoc />
    public async Task EnsureDatabasesAsync(CancellationToken ct)
    {
        // A fresh box may have no target database yet; every set shares the tool's one connection.
        var registry = provider.GetRequiredService<IMigrationSetRegistry>();
        await registry.GetDialect(registry.Names[0]).EnsureDatabaseExistsAsync(connectionString, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync().ConfigureAwait(false);
        await dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
