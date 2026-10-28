namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a session that holds one of a set's migrator locks, as <c>pg_locks</c> and <c>pg_stat_activity</c> report it.</summary>
public sealed record MigrationLockHolder
{
    /// <summary>Gets the holder's backend process id, which <c>unlock</c> terminates.</summary>
    public required int Pid { get; init; }

    /// <summary>Gets the holder's <c>application_name</c>, when it set one.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>Gets when the holder's current transaction began, else when its session connected.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Gets the holder's current or last query, cut to 200 characters.</summary>
    public string? Query { get; init; }

    /// <summary>Gets which key it holds: <c>reserved</c> or <c>legacy</c>.</summary>
    public required string Key { get; init; }
}
