using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Parses and validates a seed file's JSON.</summary>
/// <remarks>
/// The shape, every name snake_case:
/// <code>
/// {
///   "mode": "upsert",
///   "tables": [
///     { "table": "plans", "key": ["code"], "rows": [ { "code": "free", "max_codes": 3 } ] }
///   ]
/// }
/// </code>
///   - <c>mode</c>: <c>upsert</c> (the file wins) or <c>insert</c> (an existing row is left alone)
///   - <c>table</c>: a table in the set's schema, or <c>schema.table</c>; <c>key</c>: the columns of a unique constraint every row carries
///   - values bind as parameters; an object or array binds as its JSON text, for a <c>jsonb</c> column
/// </remarks>
internal static class MigrationSeedParser
{
    /// <summary>Parses a seed file.</summary>
    /// <param name="raw">The file as read.</param>
    /// <param name="hasher">The checksum hasher the journal compares with.</param>
    /// <returns>The validated file, or a <c>Validation</c> failure naming the problem.</returns>
    public static Result<MigrationSeedFile> Parse(RawSeed raw, IMigrationChecksumHasher hasher)
    {
        var match = MigrationConstants.FolderPattern().Match(raw.Name);
        if (!match.Success)
            return Invalid(raw.Name, "the file name must match NNN-name.json (e.g. 001-plans.json)");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw.Json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            return Invalid(raw.Name, $"the file is not JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid(raw.Name, "the root must be an object with 'mode' and 'tables'");

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("mode" or "tables" or "description"))
                    return Invalid(raw.Name, $"unknown property '{property.Name}'; known: mode, tables, description");
            }

            MigrationSeedMode mode;
            switch (root.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() : null)
            {
                case "upsert": mode = MigrationSeedMode.Upsert; break;
                case "insert": mode = MigrationSeedMode.Insert; break;
                default: return Invalid(raw.Name, "'mode' must be \"upsert\" or \"insert\"");
            }

            if (!root.TryGetProperty("tables", out var tablesElement) || tablesElement.ValueKind != JsonValueKind.Array || tablesElement.GetArrayLength() == 0)
                return Invalid(raw.Name, "'tables' must be a non-empty array");

            var tables = new List<MigrationSeedTable>();
            foreach (var tableElement in tablesElement.EnumerateArray())
            {
                if (ParseTable(raw.Name, tableElement).IsFailure(out var error, out var table))
                    return Result<MigrationSeedFile>.Fail(error);
                tables.Add(table);
            }

            return Result<MigrationSeedFile>.Ok(new MigrationSeedFile(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), raw.Name, hasher.Hash(raw.Json), mode, tables));
        }
    }

    private static Result<MigrationSeedTable> ParseTable(string file, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return InvalidTable(file, "each entry of 'tables' must be an object with 'table', 'key' and 'rows'");

        var name = element.TryGetProperty("table", out var tableElement) && tableElement.ValueKind == JsonValueKind.String ? tableElement.GetString()! : "";
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var schema = dot < 0 ? null : name[..dot];
        var table = dot < 0 ? name : name[(dot + 1)..];
        if (!IsIdentifier(table) || (schema is not null && !IsIdentifier(schema)))
            return InvalidTable(file, $"'table' must be a snake_case table or schema.table; found '{name}'");

        if (!element.TryGetProperty("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.Array || keyElement.GetArrayLength() == 0)
            return InvalidTable(file, $"table '{name}' needs 'key': the columns of a unique constraint its rows conflict on");

        var key = new List<string>();
        foreach (var column in keyElement.EnumerateArray())
        {
            if (column.ValueKind != JsonValueKind.String || !IsIdentifier(column.GetString()))
                return InvalidTable(file, $"table '{name}' has a key column that is not a snake_case name");
            key.Add(column.GetString()!);
        }

        if (!element.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
            return InvalidTable(file, $"table '{name}' needs 'rows': an array of objects");

        var rows = new List<IReadOnlyList<KeyValuePair<string, JsonElement>>>();
        foreach (var rowElement in rowsElement.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Object)
                return InvalidTable(file, $"table '{name}' has a row that is not an object");

            var row = new List<KeyValuePair<string, JsonElement>>();
            foreach (var column in rowElement.EnumerateObject())
            {
                if (!IsIdentifier(column.Name))
                    return InvalidTable(file, $"table '{name}' has a column '{column.Name}' that is not a snake_case name");
                row.Add(new KeyValuePair<string, JsonElement>(column.Name, column.Value.Clone()));
            }

            var missing = key.Where(column => row.All(pair => pair.Key != column)).ToList();
            if (missing.Count > 0)
                return InvalidTable(file, $"table '{name}' has a row without its key column(s) {string.Join(", ", missing)}");
            rows.Add(row);
        }

        return Result<MigrationSeedTable>.Ok(new MigrationSeedTable(schema, table, key, rows));
    }

    private static bool IsIdentifier(string? name) => name is not null && MigrationConstants.IdentifierPattern().IsMatch(name);

    private static Result<MigrationSeedFile> Invalid(string file, string reason) =>
        Result<MigrationSeedFile>.Fail(AppErrorFactory.Validation($"Seed file '{file}': {reason}."));

    private static Result<MigrationSeedTable> InvalidTable(string file, string reason) =>
        Result<MigrationSeedTable>.Fail(AppErrorFactory.Validation($"Seed file '{file}': {reason}."));
}
