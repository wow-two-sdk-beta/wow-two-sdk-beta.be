namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a table a set publishes to other owners: the keys they may reference and the columns they may read.</summary>
/// <remarks>
///   - the owner changes a published table only by expand and contract, so a referencing set keeps working
///   - the <c>Testing.Data</c> lint refuses a key or mapping onto a table no owner lists
/// </remarks>
/// <param name="Table">The table name, snake_case, in the set's schema.</param>
/// <param name="Keys">The columns another set may key into.</param>
/// <param name="ReadableColumns">The columns another set may read.</param>
public sealed record MigrationReferenceableTable(string Table, IReadOnlyList<string> Keys, IReadOnlyList<string> ReadableColumns);
