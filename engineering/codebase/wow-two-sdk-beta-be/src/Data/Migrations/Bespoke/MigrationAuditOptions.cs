namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds who and which job runs a writing verb, recorded on every event row (<c>rollback</c>, <c>forget</c>, <c>repair</c>, <c>baseline</c>, <c>claim</c>, <c>unlock</c>).</summary>
/// <remarks>
///   - unset values fill from configuration <see cref="MigrationConstants.ByConfigurationKey"/> and <see cref="MigrationConstants.JobConfigurationKey"/>,
///     else the process variables <c>Migrations__By</c> and <c>Migrations__Job</c>
///   - <see cref="By"/> falls back to <c>{user}@{machine}</c>; <see cref="Job"/> stays null
///   - a deploy runner sets <c>Migrations__Job</c> to its job id and <c>Migrations__By</c> to the operator
/// </remarks>
public sealed record MigrationAuditOptions
{
    /// <summary>Gets or sets who runs the verb, such as an operator's name or <c>wheelhouse</c>.</summary>
    public string? By { get; set; }

    /// <summary>Gets or sets the job that runs the verb, such as a Wheelhouse job id.</summary>
    public string? Job { get; set; }
}
