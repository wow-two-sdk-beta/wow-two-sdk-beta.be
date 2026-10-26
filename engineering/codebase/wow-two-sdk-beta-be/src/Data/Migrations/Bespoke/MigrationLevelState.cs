namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes how a set's applied level compares with what the running code expects, most severe first.</summary>
public enum MigrationLevelState
{
    /// <summary>Every source migration is applied and nothing else is recorded.</summary>
    Current,

    /// <summary>Source migrations are pending: the database is older than the code.</summary>
    Behind,

    /// <summary>The history holds a migration above the code's highest ordinal: the database is newer than the code.</summary>
    Ahead,

    /// <summary>The history holds migrations absent from the source below the code's highest ordinal, and orphans are not allowed.</summary>
    Orphaned,

    /// <summary>An applied migration's source changed since it ran.</summary>
    Drifted,

    /// <summary>The schema's marker names another set or owner, so this host must not migrate or use it.</summary>
    OwnerConflict,
}
