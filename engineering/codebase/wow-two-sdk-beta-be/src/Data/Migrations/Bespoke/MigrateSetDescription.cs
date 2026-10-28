namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Describes one set an image carries, read without settings or a database — an entry of <c>migrate sets --json</c>.</summary>
/// <remarks>A deploy pipeline records it per image, so the release manifest names each set's level, contracts and dependencies.</remarks>
public sealed record MigrateSetDescription
{
    /// <summary>Gets the set name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the set's schema.</summary>
    public required string Schema { get; init; }

    /// <summary>Gets the strategy that applies the set: <c>bespoke</c>, <c>ef</c> or <c>dbup</c>.</summary>
    public required string Strategy { get; init; }

    /// <summary>Gets the kind of owner the set claims its schema as, such as <c>module</c> or <c>service</c>; null claims nothing.</summary>
    public string? OwnerKind { get; init; }

    /// <summary>Gets the set's level: the highest embedded ordinal, or the number of migrations for a strategy without ordinals.</summary>
    public required int Level { get; init; }

    /// <summary>Gets every contract the set carries, with its <c>since</c>.</summary>
    public IReadOnlyList<MigrateSetContract> Contracts { get; init; } = [];

    /// <summary>Gets every <c>-- @requires</c> the set carries.</summary>
    public IReadOnlyList<MigrateSetRequirement> Requires { get; init; } = [];

    /// <summary>Gets the sets this set references and the level each must reach first.</summary>
    public IReadOnlyList<MigrationDependency> DependsOn { get; init; } = [];

    /// <summary>Gets the extensions the database must already have.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Gets the tables the set publishes to other owners.</summary>
    public IReadOnlyList<MigrationReferenceableTable> ReferenceableTables { get; init; } = [];

    /// <summary>Gets the package (assembly) that embeds the set, or null for a filesystem set.</summary>
    public string? Package { get; init; }

    /// <summary>Gets the embedding package's version, build metadata stripped, or null when unknown.</summary>
    public string? Version { get; init; }

    /// <summary>Gets the owner release a contract's <c>since</c> compares with, or null when unknown.</summary>
    public string? Release { get; init; }
}

/// <summary>Represents one contract a set carries: its label and the oldest owner release that no longer reads what it removes.</summary>
/// <param name="Label">The <c>NNN-name</c> label.</param>
/// <param name="Since">The <c>since</c> as written, such as <c>v0.5</c>.</param>
public sealed record MigrateSetContract(string Label, string Since);

/// <summary>Represents one <c>-- @requires</c> a set carries: the file and the background migration it waits for.</summary>
/// <param name="Label">The <c>NNN-name</c> label.</param>
/// <param name="Name">The background migration's name.</param>
public sealed record MigrateSetRequirement(string Label, string Name);
