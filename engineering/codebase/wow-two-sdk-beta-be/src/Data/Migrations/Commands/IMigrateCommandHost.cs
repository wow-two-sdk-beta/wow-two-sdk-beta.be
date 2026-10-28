using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Defines where the verb tree runs: an image's own services, or the <c>wow-migrate</c> tool's provider built per invocation.</summary>
internal interface IMigrateCommandHost
{
    /// <summary>Opens the services one invocation runs over.</summary>
    /// <param name="invocation">The verb, its set and its parse result.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The scope, or a failure the tree reports as refused (exit <c>1</c>), such as an invalid manifest.</returns>
    Task<Result<IMigrateCommandScope>> OpenAsync(MigrateInvocation invocation, CancellationToken ct);
}
