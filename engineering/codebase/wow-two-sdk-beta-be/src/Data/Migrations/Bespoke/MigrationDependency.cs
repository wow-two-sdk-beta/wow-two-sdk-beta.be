namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a set this set references, such as a key into Identity's tables, and the level it needs.</summary>
/// <remarks>Declared through <see cref="MigrationOptions.DependsOn"/>; apply runs the referenced set first and refuses before any SQL while it is missing or below the level.</remarks>
/// <param name="Set">The referenced set's name.</param>
/// <param name="Level">The lowest applied level of the referenced set this set's scripts need.</param>
public sealed record MigrationDependency(string Set, int Level);
