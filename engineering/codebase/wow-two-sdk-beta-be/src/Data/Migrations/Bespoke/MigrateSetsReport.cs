using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a <c>sets</c> run: the run's result and one description per set.</summary>
/// <param name="Result">The run's result: one entry per set at its embedded level, or a refusal.</param>
/// <param name="Sets">One description per described set, in dependency order.</param>
public sealed record MigrateSetsReport(MigrateRunResult Result, IReadOnlyList<MigrateSetDescription> Sets)
{
    /// <summary>Renders the <c>sets --json</c> document: <c>{"sets":[…]}</c>, camelCase, nulls left out, no secret.</summary>
    public string ToJson() => JsonSerializer.Serialize(new MigrateSetsDocument(Sets), MigrateResultJsonContext.Default.MigrateSetsDocument);
}

/// <summary>Holds the <c>sets --json</c> document's shape.</summary>
/// <param name="Sets">One description per set.</param>
internal sealed record MigrateSetsDocument(IReadOnlyList<MigrateSetDescription> Sets);
