using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Holds the global options that pick the target database and the sets a verb runs.</summary>
/// <param name="Connection">The <c>--connection</c> value, or null.</param>
/// <param name="SqlDir">The <c>--sql-dir</c> value, or null.</param>
/// <param name="SetsFile">The <c>--sets</c> manifest path, or null.</param>
/// <param name="Set">The <c>--set</c> name, or null.</param>
/// <param name="Schema">The <c>--schema</c> value, or null.</param>
/// <param name="SchemaOwner">The <c>--schema-owner</c> value, or null.</param>
/// <param name="OwnerInstance">The <c>--owner-instance</c> value, or null.</param>
internal sealed record CliTarget(
    string? Connection, string? SqlDir, string? SetsFile, string? Set, string? Schema, string? SchemaOwner, string? OwnerInstance = null);

/// <summary>Describes one migration set the CLI registers: its source, schema, owner and owner instance.</summary>
/// <param name="Name">The set name; <see cref="MigrationConstants.DefaultSetName"/> is the product's own set.</param>
/// <param name="SqlDir">The on-disk migrations root, or null when the set reads an assembly.</param>
/// <param name="AssemblyPath">The assembly embedding the set's scripts, or null when the set reads a directory.</param>
/// <param name="ResourcePrefix">The embedded-resource folder prefix inside <paramref name="AssemblyPath"/>.</param>
/// <param name="Schema">The set's schema, or null for the default.</param>
/// <param name="SchemaOwner">The owner the set claims its schema under, or null.</param>
/// <param name="HistoryTable">The history table, or null for the set's default.</param>
/// <param name="OwnerInstance">The instance the set claims its schema as, or null; an owned set needs one.</param>
internal sealed record CliMigrationSet(
    string Name, string? SqlDir, string? AssemblyPath, string ResourcePrefix, string? Schema, string? SchemaOwner, string? HistoryTable,
    string? OwnerInstance = null)
{
    /// <summary>The embedded-resource prefix a set reads when the manifest names none.</summary>
    public const string DefaultResourcePrefix = "Migrations/";

    /// <summary>Returns whether a name is a valid set name for the CLI: the default set or a named-set identifier.</summary>
    /// <param name="name">The set name.</param>
    public static bool IsValidName(string name) =>
        name == MigrationConstants.DefaultSetName || MigrationConstants.SetNamePattern().IsMatch(name);
}

/// <summary>Reads a migration-sets manifest: the JSON file that lists every set a deploy step runs.</summary>
/// <remarks>
///   - shape: <c>{ "sets": [ { "name", "sqlDir" | "assembly", "resourcePrefix", "schema", "schemaOwner", "ownerInstance", "historyTable" } ] }</c>
///   - relative paths resolve against the manifest's directory
/// </remarks>
internal static class CliMigrationSetManifest
{
    /// <summary>Reads and validates the manifest at <paramref name="path"/>.</summary>
    /// <param name="path">The manifest path.</param>
    /// <returns>The sets in manifest order, or a <c>FileNotFound</c> or <c>Validation</c> failure.</returns>
    public static Result<IReadOnlyList<CliMigrationSet>> Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.FileNotFound($"Migration-sets manifest '{fullPath}' does not exist."));

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sets", out var setsElement)
                || setsElement.ValueKind != JsonValueKind.Array)
            {
                return Invalid(fullPath, "it must be an object with a \"sets\" array");
            }

            var directory = Path.GetDirectoryName(fullPath)!;
            var sets = new List<CliMigrationSet>();
            foreach (var element in setsElement.EnumerateArray())
            {
                if (ReadSet(element, directory).IsFailure(out var error, out var set))
                    return Invalid(fullPath, error.Message);
                if (sets.Any(existing => existing.Name == set.Name))
                    return Invalid(fullPath, $"set '{set.Name}' appears twice");
                sets.Add(set);
            }

            return sets.Count == 0 ? Invalid(fullPath, "it lists no sets") : Result<IReadOnlyList<CliMigrationSet>>.Ok(sets);
        }
        catch (JsonException exception)
        {
            return Invalid(fullPath, exception.Message);
        }
    }

    private static Result<CliMigrationSet> ReadSet(JsonElement element, string directory)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return Result<CliMigrationSet>.Fail(AppErrorFactory.Validation("every set must be an object"));

        var name = Text(element, "name");
        if (name is null || !CliMigrationSet.IsValidName(name))
            return Result<CliMigrationSet>.Fail(AppErrorFactory.Validation($"set name '{name}' must be 'default' or a lowercase identifier"));

        var sqlDir = Text(element, "sqlDir");
        var assembly = Text(element, "assembly");
        if ((sqlDir is null) == (assembly is null))
            return Result<CliMigrationSet>.Fail(AppErrorFactory.Validation($"set '{name}' needs exactly one of \"sqlDir\" and \"assembly\""));

        return Result<CliMigrationSet>.Ok(new CliMigrationSet(
            name,
            sqlDir is null ? null : Path.GetFullPath(sqlDir, directory),
            assembly is null ? null : Path.GetFullPath(assembly, directory),
            Text(element, "resourcePrefix") ?? CliMigrationSet.DefaultResourcePrefix,
            Text(element, "schema"),
            Text(element, "schemaOwner"),
            Text(element, "historyTable"),
            Text(element, "ownerInstance")));
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Result<IReadOnlyList<CliMigrationSet>> Invalid(string path, string reason) =>
        Result<IReadOnlyList<CliMigrationSet>>.Fail(AppErrorFactory.Validation($"Migration-sets manifest '{path}' is invalid: {reason}."));
}
