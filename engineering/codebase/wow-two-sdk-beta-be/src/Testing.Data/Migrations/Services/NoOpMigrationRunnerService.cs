using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Services;

/// <summary>Provides no-op migrations for tests whose schema is created outside the bespoke migrator.</summary>
/// <remarks>Replace with the real <see cref="IMigrationRunnerService"/> when the test exercises migrations.</remarks>
internal sealed class NoOpMigrationRunnerService : IMigrationRunnerService
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> ApplyPendingAsync(string appliedBy, CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Ok([]));

    /// <inheritdoc />
    public Task<Result<MigrationDryRunReport>> DryRunPendingAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<MigrationDryRunReport>.Ok(
            new MigrationDryRunReport { WouldApply = [], Succeeded = [], NotRehearsed = [] }));

    /// <inheritdoc />
    public Task<Result<MigrationStatus>> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<MigrationStatus>.Ok(
            new MigrationStatus { Applied = [], Pending = [], Drifted = [], Orphaned = [] }));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> RollbackAsync(int? targetOrdinal = null, CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Ok([]));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> RepairAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Ok([]));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> BaselineAsync(int? throughOrdinal = null, CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Ok([]));
}
