namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents one contract a set carries: its label and the oldest owner release that no longer reads what it removes.</summary>
/// <param name="Label">The <c>NNN-name</c> label.</param>
/// <param name="Since">The <c>since</c> as written, such as <c>v0.5</c>.</param>
public sealed record MigrateSetContract(string Label, string Since);
