namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a validated seed file: one change's rows across every table it fills, applied in one transaction and journalled once.</summary>
/// <param name="Ordinal">The <c>NNN</c> ordinal, the apply order.</param>
/// <param name="Name">The <c>NNN-name</c> label the journal keys on.</param>
/// <param name="Checksum">The SHA-256 over the normalized file text.</param>
/// <param name="Mode">Who owns a seeded row once it exists.</param>
/// <param name="Tables">The tables, in fill order.</param>
internal sealed record MigrationSeedFile(int Ordinal, string Name, string Checksum, MigrationSeedMode Mode, IReadOnlyList<MigrationSeedTable> Tables);
