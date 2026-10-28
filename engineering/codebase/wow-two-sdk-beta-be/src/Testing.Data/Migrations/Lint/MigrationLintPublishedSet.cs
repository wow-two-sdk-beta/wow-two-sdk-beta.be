using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

/// <summary>Represents the tables a set publishes, declared to the lint when the provider does not register the set.</summary>
/// <param name="Schema">The set's schema.</param>
/// <param name="Tables">The published tables.</param>
public sealed record MigrationLintPublishedSet(string Schema, IReadOnlyList<MigrationReferenceableTable> Tables);
