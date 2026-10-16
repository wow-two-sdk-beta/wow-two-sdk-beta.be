namespace WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke;

/// <summary>Describes how pending transactional migrations share transactions during apply.</summary>
public enum MigrationTransactionScope
{
    /// <summary>Consecutive transactional migrations and their history rows commit together; one failure rolls back the whole batch.</summary>
    /// <remarks>A <c>-- @no-transaction</c> script commits the batch before it, runs bare, and starts the next batch.</remarks>
    Batch,

    /// <summary>Each transactional migration commits on its own; a failure keeps every earlier migration applied.</summary>
    PerMigration,
}
