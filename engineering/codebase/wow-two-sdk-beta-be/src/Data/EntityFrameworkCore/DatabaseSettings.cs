namespace WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;

/// <summary>Holds configuration for the database connection, bound from the <c>DatabaseSettings</c> section.</summary>
public sealed record DatabaseSettings
{
    /// <summary>Gets the database connection string.</summary>
    public required string ConnectionString { get; init; }
}
