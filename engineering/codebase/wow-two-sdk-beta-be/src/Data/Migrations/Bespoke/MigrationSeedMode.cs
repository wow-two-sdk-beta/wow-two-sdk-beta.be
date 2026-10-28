using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes who owns a seeded row once it exists: the file, or the runtime.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MigrationSeedMode>))]
public enum MigrationSeedMode
{
    /// <summary>The file wins: an existing row with the same key takes the file's values.</summary>
    [JsonStringEnumMemberName("upsert")]
    Upsert,

    /// <summary>The runtime owns the row: an existing row with the same key is left alone.</summary>
    [JsonStringEnumMemberName("insert")]
    Insert,
}
