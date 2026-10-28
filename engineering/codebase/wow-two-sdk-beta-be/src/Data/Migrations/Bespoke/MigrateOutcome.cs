using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes how a verb ended for one set or for the whole run; each maps to one exit code.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MigrateOutcome>))]
public enum MigrateOutcome
{
    /// <summary>The verb found its target state and changed nothing — exit <c>0</c>.</summary>
    [JsonStringEnumMemberName("current")]
    Current,

    /// <summary>The verb changed what it was asked to — exit <c>0</c>.</summary>
    [JsonStringEnumMemberName("done")]
    Done,

    /// <summary>The run stopped at an earlier set, so this set was not attempted — no exit of its own.</summary>
    [JsonStringEnumMemberName("skipped")]
    Skipped,

    /// <summary>A lock was not acquired within its bound; a retry may pass — exit <c>3</c>.</summary>
    [JsonStringEnumMemberName("busy")]
    Busy,

    /// <summary>A rule refused the verb before it changed anything: drift, an orphan, an owner conflict, an incompatible ahead, a missing dependency or extension — exit <c>1</c>.</summary>
    [JsonStringEnumMemberName("refused")]
    Refused,

    /// <summary>A script, a seed or the connection failed — exit <c>2</c>.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}
