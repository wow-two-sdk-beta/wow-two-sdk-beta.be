using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Defines a set another strategy applies — an EF context or a DbUp journal — answering the verbs its runner can serve.</summary>
/// <remarks>
///   - serves <c>check</c>, <c>plan</c>, <c>apply</c> and <c>sets</c> with the bespoke exits and result entries; <c>seed</c> finds nothing to seed
///   - the tree refuses every other verb for it with exit <c>1</c>
///   - registered by <c>AddEfMigrationsRunner</c> and <c>AddDbUpRunner</c>; its runner's journal has no kinds, so history this image lacks counts as an incompatible ahead
/// </remarks>
internal interface IMigrationStrategySet
{
    /// <summary>Gets the set name, unique across the host's sets.</summary>
    string Name { get; }

    /// <summary>Gets the strategy: <c>ef</c> or <c>dbup</c>.</summary>
    string Strategy { get; }

    /// <summary>Gets the schema the set's journal lives in.</summary>
    string Schema { get; }

    /// <summary>Describes the set from the migrations the image carries; never connects.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateSetDescription> DescribeAsync(CancellationToken ct);

    /// <summary>Compares the journal with the image read-only: current when it may run, else refused.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateSetResult> CheckAsync(CancellationToken ct);

    /// <summary>Lists the pending migrations read-only.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateSetResult> PlanAsync(CancellationToken ct);

    /// <summary>Applies the pending migrations; refuses a dry run, which the runner cannot rehearse.</summary>
    /// <param name="dryRun">Whether a rehearsal was asked for.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<MigrateSetResult> ApplyAsync(bool dryRun, CancellationToken ct);
}
