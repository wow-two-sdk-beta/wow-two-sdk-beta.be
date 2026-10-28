namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents one advisory-lock key a set's migrator takes: the two-key form in the reserved class, or the legacy single bigint.</summary>
/// <remarks>
///   - PostgreSQL keeps the two-key space apart from the single-key space, so an app's <c>pg_advisory_xact_lock(bigint)</c> never meets the reserved key
///   - the legacy key is the set's <see cref="MigrationOptions.AdvisoryLockId"/>, which migrators before the reserved class took
/// </remarks>
public readonly record struct MigrationLockKey
{
    private MigrationLockKey(int? keyClass, long key)
    {
        KeyClass = keyClass;
        Key = key;
    }

    /// <summary>Gets the reserved class of a two-key lock, or null for the legacy single key.</summary>
    public int? KeyClass { get; }

    /// <summary>Gets the key: the 32-bit key within the class, or the legacy 64-bit id.</summary>
    public long Key { get; }

    /// <summary>Gets whether this is the legacy single bigint key.</summary>
    public bool IsLegacy => KeyClass is null;

    /// <summary>Gets the key in the reserved class for a set's lock id, folding the 64-bit id into 32 bits.</summary>
    /// <param name="advisoryLockId">The set's <see cref="MigrationOptions.AdvisoryLockId"/>.</param>
    public static MigrationLockKey Reserved(long advisoryLockId) =>
        new(MigrationConstants.AdvisoryLockClass, unchecked((int)(advisoryLockId ^ (advisoryLockId >>> 32))));

    /// <summary>Gets the legacy single bigint key a set's migrator took before the reserved class.</summary>
    /// <param name="advisoryLockId">The set's <see cref="MigrationOptions.AdvisoryLockId"/>.</param>
    public static MigrationLockKey Legacy(long advisoryLockId) => new(null, advisoryLockId);

    /// <summary>Gets the keys a set's migrator holds, the reserved one first.</summary>
    /// <param name="options">The set's options.</param>
    public static IReadOnlyList<MigrationLockKey> For(MigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TakeLegacyAdvisoryLock
            ? [Reserved(options.AdvisoryLockId), Legacy(options.AdvisoryLockId)]
            : [Reserved(options.AdvisoryLockId)];
    }

    /// <summary>Describes the key for logs, such as <c>reserved(2000842087, 12345)</c> or <c>legacy(4855178001)</c>.</summary>
    public override string ToString() => KeyClass is { } keyClass ? $"reserved({keyClass}, {Key})" : $"legacy({Key})";
}
