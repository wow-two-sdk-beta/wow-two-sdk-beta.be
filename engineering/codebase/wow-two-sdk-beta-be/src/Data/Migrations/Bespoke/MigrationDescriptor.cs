namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a validated, ordered migration with parsed ordinal, name, checksum, and header directives.</summary>
public sealed record MigrationDescriptor
{
    /// <summary>Gets the numeric ordinal parsed from the <c>NNN-</c> folder prefix — the apply gate and ordering key.</summary>
    public required int Ordinal { get; init; }

    /// <summary>Gets the descriptive name (folder text after <c>NNN-</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the Apply script body.</summary>
    public required string ApplySql { get; init; }

    /// <summary>Gets the Rollback script body — every migration ships one.</summary>
    public required string RollbackSql { get; init; }

    /// <summary>Gets the SHA-256 over the normalized Apply script.</summary>
    public required string Checksum { get; init; }

    /// <summary>Gets whether the Apply script runs outside a transaction, declared via a leading no-transaction directive.</summary>
    public required bool NoTransaction { get; init; }

    /// <summary>Gets whether the release before survives the migration: a contract when the header says <c>-- @contract</c>.</summary>
    public MigrationKind Kind { get; init; } = MigrationKind.Expand;

    /// <summary>Gets a contract's <c>since</c> as written, such as <c>v0.5</c>: the oldest owner release that no longer reads what it removes.</summary>
    public string? ContractSince { get; init; }

    /// <summary>Gets the background migrations <c>-- @requires</c> names; apply refuses the file until each has finished.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>Gets the <c>-- @lock-timeout</c> override, or null to use <see cref="MigrationOptions.LockTimeout"/>.</summary>
    public TimeSpan? LockTimeout { get; init; }

    /// <summary>Gets the <c>NNN-name</c> label for logs and status.</summary>
    public string Label => $"{Ordinal:D3}-{Name}";
}
