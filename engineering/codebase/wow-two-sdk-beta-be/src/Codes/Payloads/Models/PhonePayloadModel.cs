namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads.Models;

/// <summary>Represents supplied data for PhonePayload encoding.</summary>
public sealed record PhonePayloadModel
{
    /// <summary>Gets the Phone value.</summary>
    public string Phone { get; init; } = string.Empty;
}
