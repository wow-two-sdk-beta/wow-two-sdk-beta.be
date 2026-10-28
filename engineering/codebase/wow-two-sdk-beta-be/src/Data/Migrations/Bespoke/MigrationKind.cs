using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes whether the release before a migration survives it: an expand it survives, or a contract it may not.</summary>
/// <remarks>History stores the kind as <c>expand</c> or <c>contract</c>; a row recorded before kinds existed stores none and counts as incompatible.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MigrationKind>))]
public enum MigrationKind
{
    /// <summary>A change the previous release survives — every file without a <c>-- @contract</c> header.</summary>
    [JsonStringEnumMemberName("expand")]
    Expand,

    /// <summary>A change an older release may not survive, marked <c>-- @contract since=vX.Y</c>.</summary>
    [JsonStringEnumMemberName("contract")]
    Contract,
}
