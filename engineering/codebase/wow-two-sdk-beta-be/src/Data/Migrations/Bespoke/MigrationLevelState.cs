using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes how a set's applied level compares with what the running code expects.</summary>
/// <remarks>A report carries the most severe state: owner conflict, incompatible ahead, drifted, orphaned, behind, compatible ahead, current.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MigrationLevelState>))]
public enum MigrationLevelState
{
    /// <summary>Every source migration is applied and nothing else is recorded.</summary>
    [JsonStringEnumMemberName("current")]
    Current,

    /// <summary>Source migrations are pending: the database is older than the code.</summary>
    [JsonStringEnumMemberName("behind")]
    Behind,

    /// <summary>The history holds migrations above the code's level that this release survives: expands, or contracts whose <c>since</c> it has reached.</summary>
    /// <remarks>The release may run: a deployed boot warns and starts, and apply proceeds.</remarks>
    [JsonStringEnumMemberName("ahead-compatible")]
    AheadCompatible,

    /// <summary>The history holds a migration above the code's level this release may not survive: a contract above it, or a row with no kind.</summary>
    [JsonStringEnumMemberName("ahead-incompatible")]
    AheadIncompatible,

    /// <summary>The history holds migrations absent from the source below the code's highest ordinal, and orphans are not allowed.</summary>
    [JsonStringEnumMemberName("orphaned")]
    Orphaned,

    /// <summary>An applied migration's source changed since it ran.</summary>
    [JsonStringEnumMemberName("drifted")]
    Drifted,

    /// <summary>The schema's marker names another set or owner, so this host must not migrate or use it.</summary>
    [JsonStringEnumMemberName("owner-conflict")]
    OwnerConflict,
}
