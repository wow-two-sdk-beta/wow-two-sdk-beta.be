using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data;

/// <summary>Runs the verb tree over an image's own services: the sets its host registered, the migrate key turned on for the run.</summary>
/// <param name="services">The image's root provider; the tree never disposes it.</param>
/// <param name="appliedBy">The stamp recorded on rows <c>apply</c> and <c>seed</c> write.</param>
internal sealed class ServiceProviderMigrateCommandHost(IServiceProvider services, string appliedBy) : IMigrateCommandHost
{
    /// <inheritdoc />
    public Task<Result<IMigrateCommandScope>> OpenAsync(MigrateInvocation invocation, CancellationToken ct)
    {
        // Before any set's runner resolves, so Migrations:ConnectionString, when set, connects every set for this run.
        services.GetService<MigrationRunMode>()?.EnterMigrateCommand();
        return Task.FromResult(Result<IMigrateCommandScope>.Ok(new Scope(services, appliedBy)));
    }

    /// <summary>Exposes the image's provider; creating databases goes through the host's database settings or the migrate key.</summary>
    /// <param name="services">The image's root provider.</param>
    /// <param name="appliedBy">The stamp recorded on applied rows.</param>
    private sealed class Scope(IServiceProvider services, string appliedBy) : IMigrateCommandScope
    {
        /// <inheritdoc />
        public IServiceProvider Services => services;

        /// <inheritdoc />
        public string AppliedBy => appliedBy;

        /// <inheritdoc />
        public Task EnsureDatabasesAsync(CancellationToken ct) => services.EnsureSetDatabasesExistAsync(ct);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
