namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds the verbs a set's event table records — every writing verb but <c>apply</c>, whose history rows are its record.</summary>
public static class MigrationEventVerbs
{
    /// <summary>Holds the verb of a rollback that ran <c>Rollback.sql</c> files and deleted their rows.</summary>
    public const string Rollback = "rollback";

    /// <summary>Holds the verb of a forget that deleted history rows without running SQL.</summary>
    public const string Forget = "forget";

    /// <summary>Holds the verb of a repair that re-recorded drifted checksums.</summary>
    public const string Repair = "repair";

    /// <summary>Holds the verb of a baseline that recorded migrations as applied without running them.</summary>
    public const string Baseline = "baseline";

    /// <summary>Holds the verb of a claim that recorded or replaced the schema's owner marker.</summary>
    public const string Claim = "claim";

    /// <summary>Holds the verb of an unlock that ended a session holding the set's lock.</summary>
    public const string Unlock = "unlock";

    /// <summary>Holds the verb of a background-migration control: pause, resume, retry or rollback.</summary>
    public const string Background = "bg";
}
