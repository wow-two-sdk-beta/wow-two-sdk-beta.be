namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads.Models;

/// <summary>Represents supplied data for GeoPayload encoding.</summary>
public sealed record GeoPayloadModel
{
    /// <summary>Gets the Latitude value.</summary>
    public double Latitude { get; init; }

    /// <summary>Gets the Longitude value.</summary>
    public double Longitude { get; init; }
}
