namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes what the startup level check does when a set's database level differs from the code's.</summary>
public enum MigrationLevelPolicy
{
    /// <summary>Stop the host: the deploy's migrate step did not run, or the database is newer than this build.</summary>
    Fail,

    /// <summary>Log a warning and start anyway.</summary>
    Warn,
}
