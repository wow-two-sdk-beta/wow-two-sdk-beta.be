namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds whether this process runs a migrate command — the switch that turns on the migrate key for every set.</summary>
/// <remarks>
///   - registered by every set registration, off; the <c>migrate</c> command turns it on before it resolves any set's runner
///   - while it is on, <see cref="MigrationConstants.ConnectionStringConfigurationKey"/>, when set, connects every set; a boot never turns it on
///   - a runner resolved before the switch keeps the connection it was built with
/// </remarks>
public sealed class MigrationRunMode
{
    /// <summary>Gets whether a migrate command runs in this process.</summary>
    public bool IsMigrateCommand { get; private set; }

    /// <summary>Marks this process as running a migrate command; idempotent.</summary>
    public void EnterMigrateCommand() => IsMigrateCommand = true;
}
