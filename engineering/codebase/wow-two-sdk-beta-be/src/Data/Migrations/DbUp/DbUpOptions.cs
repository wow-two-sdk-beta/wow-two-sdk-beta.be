using System.Reflection;
using DbUp.Builder;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.DbUp;

/// <summary>Holds options for the DbUp startup runner.</summary>
public sealed record DbUpOptions
{
    /// <summary>Gets a value indicating whether the runner is enabled. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the assembly to scan for embedded <c>.sql</c> scripts. Defaults to the entry assembly when null.</summary>
    public Assembly? ScriptsAssembly { get; set; }

    /// <summary>Gets an optional namespace prefix to filter scripts (e.g. <c>"App.Migrations.Scripts."</c>).</summary>
    public string? ScriptsNamespacePrefix { get; set; }

    /// <summary>Gets a delegate that builds the <see cref="UpgradeEngineBuilder"/> for the target provider. Required — picks Postgres / SqlServer / MySql / Sqlite.</summary>
    public Func<string, UpgradeEngineBuilder>? UpgradeEngineFactory { get; set; }

    /// <summary>Gets the connection string for the target database.</summary>
    public required string ConnectionString { get; set; }

    /// <summary>Gets the name the journal's set answers to in the migrate tree (<c>--set</c>, the result line). Default <c>default</c>, the product's own set.</summary>
    /// <remarks>Rename it when the host also registers a bespoke default set: two sets never share a name.</remarks>
    public string SetName { get; set; } = "default";

    /// <summary>Gets the schema the journal lives in, as the migrate tree reports it. Default <c>public</c>.</summary>
    public string SchemaName { get; set; } = "public";
}
