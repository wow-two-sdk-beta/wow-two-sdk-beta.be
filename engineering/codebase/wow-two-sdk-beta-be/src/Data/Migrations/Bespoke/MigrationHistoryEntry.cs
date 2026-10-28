namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents one applied migration recorded in the history table, mapped by Dapper (snake_case).</summary>
public sealed class MigrationHistoryEntry
{
    /// <summary>Gets or sets the migration ordinal (primary key).</summary>
    public int Ordinal { get; set; }

    /// <summary>Gets or sets the version label active when the migration was applied.</summary>
    public string Version { get; set; } = "";

    /// <summary>Gets or sets the migration name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the normalized Apply script checksum recorded at apply time.</summary>
    public string Checksum { get; set; } = "";

    /// <summary>Gets or sets the time the migration was applied.</summary>
    public DateTimeOffset AppliedAt { get; set; }

    /// <summary>Gets or sets which host applied the migration.</summary>
    public string AppliedBy { get; set; } = "";

    /// <summary>Gets or sets the apply duration in milliseconds.</summary>
    public long ExecutionMs { get; set; }

    /// <summary>Gets or sets the recorded kind, <c>expand</c> or <c>contract</c>; null on a row recorded before kinds existed, which counts as incompatible.</summary>
    public string? Kind { get; set; }

    /// <summary>Gets or sets a contract's <c>since</c> as written, such as <c>v0.5</c>; null for an expand.</summary>
    public string? ContractSince { get; set; }

    /// <summary>Gets the <c>NNN-name</c> label.</summary>
    public string Label => $"{Ordinal:D3}-{Name}";
}
