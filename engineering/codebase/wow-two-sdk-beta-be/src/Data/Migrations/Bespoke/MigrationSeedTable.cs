using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents one table a seed file fills: its name, the key its rows conflict on, and the rows.</summary>
/// <param name="Schema">The schema the file names, or null for the set's own schema.</param>
/// <param name="Table">The table name, snake_case.</param>
/// <param name="Key">The columns of a unique constraint the rows conflict on.</param>
/// <param name="Rows">Each row's columns and JSON values, in file order.</param>
internal sealed record MigrationSeedTable(
    string? Schema,
    string Table,
    IReadOnlyList<string> Key,
    IReadOnlyList<IReadOnlyList<KeyValuePair<string, JsonElement>>> Rows);
