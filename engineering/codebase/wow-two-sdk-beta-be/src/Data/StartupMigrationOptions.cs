namespace WoW.Two.Sdk.Beta.Be.Data;

/// <summary>Holds how <see cref="PostgresPersistenceStartupExtensions.MigrateBespokeOnStartupAsync(IServiceProvider, Action{StartupMigrationOptions}?, CancellationToken)"/> treats a boot.</summary>
/// <remarks>
///   - a local run (<c>Development</c> without <c>DEPLOY_ENVIRONMENT</c>) always applies at boot
///   - any other boot verifies levels read-only unless <see cref="ApplyWhenDeployed"/> opts in
/// </remarks>
public sealed record StartupMigrationOptions
{
    /// <summary>Gets or sets whether a deployed host — any boot but a local run — applies every set's pending migrations at boot.</summary>
    /// <remarks>
    ///   - default <see langword="false"/>: the host only verifies levels, and the deploy's migrate step applies them
    ///   - <see langword="true"/> is an explicit opt-in, logged as a warning at every boot that uses it
    /// </remarks>
    public bool ApplyWhenDeployed { get; set; }
}
