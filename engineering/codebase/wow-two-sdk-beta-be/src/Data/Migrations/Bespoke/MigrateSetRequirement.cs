namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents one <c>-- @requires</c> a set carries: the file and the background migration it waits for.</summary>
/// <param name="Label">The <c>NNN-name</c> label.</param>
/// <param name="Name">The background migration's name.</param>
public sealed record MigrateSetRequirement(string Label, string Name);
