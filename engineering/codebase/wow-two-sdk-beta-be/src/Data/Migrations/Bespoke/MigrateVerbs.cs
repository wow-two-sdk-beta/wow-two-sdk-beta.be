namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds the verb names a <see cref="MigrateRunResult"/> reports, one per <see cref="IMigrationEngine"/> operation.</summary>
public static class MigrateVerbs
{
    /// <summary>Holds the read-only level check.</summary>
    public const string Check = "check";

    /// <summary>Holds the lock-free pending list.</summary>
    public const string Plan = "plan";

    /// <summary>Holds the forward apply, or its rehearsal.</summary>
    public const string Apply = "apply";

    /// <summary>Holds the seed-file run after apply.</summary>
    public const string Seed = "seed";

    /// <summary>Holds the confirmed rollback to a level.</summary>
    public const string Rollback = "rollback";

    /// <summary>Holds the confirmed forget above a level.</summary>
    public const string Forget = "forget";

    /// <summary>Holds the confirmed checksum repair.</summary>
    public const string Repair = "repair";

    /// <summary>Holds the confirmed baseline through a level.</summary>
    public const string Baseline = "baseline";

    /// <summary>Holds the confirmed schema claim.</summary>
    public const string Claim = "claim";

    /// <summary>Holds the read-only lock listing.</summary>
    public const string Locks = "locks";

    /// <summary>Holds the confirmed end of a lock holder's session.</summary>
    public const string Unlock = "unlock";
}
