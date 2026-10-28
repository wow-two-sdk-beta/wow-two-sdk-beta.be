namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines the migration sets a host registered — its own default set plus each module's named set — in registration order.</summary>
/// <remarks>Resolving it fails fast when two sets share one history table or claim one schema, or depend on each other in a cycle.</remarks>
public interface IMigrationSetRegistry
{
    /// <summary>Gets every set name in registration order; the default set is <see cref="MigrationConstants.DefaultSetName"/>.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Returns whether a set is registered.</summary>
    /// <param name="setName">The set name.</param>
    bool Contains(string setName);

    /// <summary>Gets the runner of one set.</summary>
    /// <param name="setName">The set name.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    IMigrationRunnerService GetRunner(string setName);

    /// <summary>Gets the effective options of one set.</summary>
    /// <param name="setName">The set name.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    MigrationOptions GetOptions(string setName);

    /// <summary>Gets the source of one set: its migrations and seed files.</summary>
    /// <param name="setName">The set name.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    IMigrationBroker GetBroker(string setName);

    /// <summary>Gets the dialect of one set.</summary>
    /// <param name="setName">The set name.</param>
    /// <exception cref="KeyNotFoundException">No set has that name.</exception>
    IMigrationDialect GetDialect(string setName);
}
