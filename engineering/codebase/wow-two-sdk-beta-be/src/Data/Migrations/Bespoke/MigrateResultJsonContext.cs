using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Serializes <see cref="MigrateRunResult"/> and the <c>sets</c> document without reflection: camelCase names, nulls left out.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MigrateRunResult))]
[JsonSerializable(typeof(MigrateSetsDocument))]
internal sealed partial class MigrateResultJsonContext : JsonSerializerContext;
