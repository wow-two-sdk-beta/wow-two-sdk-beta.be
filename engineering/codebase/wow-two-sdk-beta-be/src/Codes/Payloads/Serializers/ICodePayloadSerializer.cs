using WoW.Two.Sdk.Beta.Be.Codes.Payloads.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads.Serializers;

/// <summary>Defines serialization of typed scanner payloads.</summary>
public interface ICodePayloadSerializer
{
    /// <summary>Serializes WIFI credentials.</summary>
    Result<string> Serialize(WifiPayloadModel model);
    /// <summary>Serializes a mailto draft.</summary>
    Result<string> Serialize(MailPayloadModel model);
    /// <summary>Serializes an SMSTO message.</summary>
    Result<string> Serialize(SmsPayloadModel model);
    /// <summary>Serializes a telephone URI.</summary>
    Result<string> Serialize(PhonePayloadModel model);
    /// <summary>Serializes a WGS84 geo URI.</summary>
    Result<string> Serialize(GeoPayloadModel model);
}
