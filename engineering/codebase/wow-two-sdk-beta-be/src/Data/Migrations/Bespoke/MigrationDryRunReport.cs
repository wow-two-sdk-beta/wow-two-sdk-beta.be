namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents the outcome of rehearsing every pending migration inside one transaction that is always rolled back.</summary>
public sealed record MigrationDryRunReport
{
    /// <summary>Gets the labels of every pending migration a real apply would run, in order.</summary>
    public required IReadOnlyList<string> WouldApply { get; init; }

    /// <summary>Gets the labels of the transactional migrations that ran without error before the rollback.</summary>
    public required IReadOnlyList<string> Succeeded { get; init; }

    /// <summary>Gets the labels of the pending <c>-- @no-transaction</c> scripts, which cannot run inside the rehearsal's transaction.</summary>
    public required IReadOnlyList<string> NotRehearsed { get; init; }

    /// <summary>Gets the first migration that failed, or null when every rehearsed migration succeeded.</summary>
    /// <remarks>The rehearsal stops at the first failure; later pending migrations are not reached.</remarks>
    public MigrationDryRunFailure? Failure { get; init; }

    /// <summary>Gets whether every rehearsed migration succeeded.</summary>
    public bool Passed => Failure is null;
}

/// <summary>Represents the first migration that failed during a dry run, with the database's error.</summary>
public sealed record MigrationDryRunFailure
{
    /// <summary>Gets the <c>NNN-name</c> label of the failed migration.</summary>
    public required string Migration { get; init; }

    /// <summary>Gets the database's error message.</summary>
    public required string Error { get; init; }

    /// <summary>Gets the provider's SQLSTATE code, when it reports one.</summary>
    public string? SqlState { get; init; }

    /// <summary>Gets whether a skipped <c>-- @no-transaction</c> script precedes the failed migration, which may depend on it.</summary>
    public required bool FollowsNotRehearsed { get; init; }
}
