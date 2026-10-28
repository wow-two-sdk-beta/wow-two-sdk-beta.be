namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents a seed file as an <see cref="IMigrationBroker"/> reads it, before validation.</summary>
public sealed record RawSeed
{
    /// <summary>Gets the file name without <c>.json</c>, expected to match <c>NNN-name</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the file's JSON text.</summary>
    public required string Json { get; init; }
}
