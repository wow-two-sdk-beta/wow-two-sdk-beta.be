namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds the <c>sets --json</c> document's shape.</summary>
/// <param name="Sets">One description per set.</param>
internal sealed record MigrateSetsDocument(IReadOnlyList<MigrateSetDescription> Sets);
