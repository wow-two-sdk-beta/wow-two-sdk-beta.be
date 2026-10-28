using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Serializes <see cref="MigrateRunResult"/> without reflection: camelCase names, nulls left out.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MigrateRunResult))]
internal sealed partial class MigrateResultJsonContext : JsonSerializerContext;
