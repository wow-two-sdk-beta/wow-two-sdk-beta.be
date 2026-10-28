namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents what apply would run on one set, read without a lock and without writing anything.</summary>
public sealed record MigrationPlan
{
    /// <summary>Gets the set the plan describes.</summary>
    public required string Set { get; init; }

    /// <summary>Gets the highest ordinal the database records, or 0 when nothing is applied or the history is absent.</summary>
    public required int AppliedLevel { get; init; }

    /// <summary>Gets the highest ordinal in the code's source, or 0 when the source is empty.</summary>
    public required int ExpectedLevel { get; init; }

    /// <summary>Gets every pending source migration in apply order, with its kind and header directives.</summary>
    public required IReadOnlyList<MigrationDescriptor> Pending { get; init; }

    /// <summary>Gets the pending migrations whose ordinal sits below the applied level, which apply refuses outside a local run.</summary>
    public IReadOnlyList<MigrationDescriptor> BelowLevel => Pending.Where(migration => migration.Ordinal < AppliedLevel).ToList();
}
