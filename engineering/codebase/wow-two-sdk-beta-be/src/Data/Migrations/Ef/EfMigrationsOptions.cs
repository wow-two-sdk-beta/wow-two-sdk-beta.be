namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Ef;

/// <summary>Holds options for the EF Migrations startup runner.</summary>
public sealed record EfMigrationsOptions
{
    /// <summary>Gets a value indicating whether the runner is enabled. Default <c>true</c> — flip to <c>false</c> in production when migrations are applied out-of-band (CI step, ops job).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the maximum number of attempts to connect before giving up. Default 10. Mitigates the classic "DB not ready yet" Docker startup race.</summary>
    public int MaxConnectAttempts { get; set; } = 10;

    /// <summary>Gets the delay between connect attempts. Default 2s.</summary>
    public TimeSpan ConnectRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets the name the context's set answers to in the migrate tree (<c>--set</c>, the result line). Default <c>default</c>, the product's own set.</summary>
    /// <remarks>Rename it when the host also registers a bespoke default set: two sets never share a name.</remarks>
    public string SetName { get; set; } = "default";
}
