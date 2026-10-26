namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents the marker row that names the set and owner which claimed a schema, mapped by Dapper (snake_case).</summary>
public sealed class MigrationSchemaOwner
{
    /// <summary>Gets or sets the claimed schema (primary key).</summary>
    public string SchemaName { get; set; } = "";

    /// <summary>Gets or sets the set that claimed the schema.</summary>
    public string SetName { get; set; } = "";

    /// <summary>Gets or sets the owner the set claimed the schema under, such as <c>module</c> or <c>service</c>.</summary>
    public string Owner { get; set; } = "";

    /// <summary>Gets or sets when the claim was recorded, as ISO 8601 text.</summary>
    public string ClaimedAt { get; set; } = "";

    /// <summary>Gets or sets which host recorded the claim, such as <c>cli</c> or <c>deploy</c>.</summary>
    public string ClaimedBy { get; set; } = "";
}
