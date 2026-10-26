using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Collects the migration sets a <see cref="MigratorHarness"/> registers: the host's default set and each module's named set, in order.</summary>
/// <remarks>
///   - every set runs the harness's dialect; the configure hooks run after it, so a hook still sees and may change <see cref="MigrationOptions.Provider"/>
///   - registration order is apply order, as in a host
/// </remarks>
public sealed class MigratorSetsBuilder
{
    private readonly List<Action<IServiceCollection, DatabaseProvider>> _registrations = [];

    private MigratorSetsBuilder()
    {
    }

    /// <summary>Adds the host's default set from an on-disk migrations root.</summary>
    /// <param name="migrationsRoot">The <c>NNN-name</c> migrations root, typically a <see cref="MigrationsWorkspace.Root"/>.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    public MigratorSetsBuilder AddDefault(string migrationsRoot, Action<MigrationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(migrationsRoot);

        return Register((services, options) => services.AddDatabaseBespokeMigrations(migrationsRoot, options), configure);
    }

    /// <summary>Adds the host's default set from an assembly's embedded <c>Migrations/NNN-name/*.sql</c> resources.</summary>
    /// <param name="sqlAssembly">The assembly embedding the set's scripts.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    public MigratorSetsBuilder AddDefault(Assembly sqlAssembly, Action<MigrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(sqlAssembly);

        return Register((services, options) => services.AddDatabaseBespokeMigrations(sqlAssembly, options), configure);
    }

    /// <summary>Adds a named set from an on-disk migrations root.</summary>
    /// <param name="setName">The set name: a lowercase identifier other than <see cref="MigrationConstants.DefaultSetName"/>.</param>
    /// <param name="migrationsRoot">The set's <c>NNN-name</c> migrations root.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>, such as its schema and owner.</param>
    public MigratorSetsBuilder Add(string setName, string migrationsRoot, Action<MigrationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(migrationsRoot);

        return Register((services, options) => services.AddDatabaseBespokeMigrationSet(setName, migrationsRoot, options), configure);
    }

    /// <summary>Adds a named set from an assembly's embedded <c>Migrations/NNN-name/*.sql</c> resources, such as a domain module's.</summary>
    /// <param name="setName">The set name.</param>
    /// <param name="sqlAssembly">The assembly embedding the set's scripts.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    public MigratorSetsBuilder Add(string setName, Assembly sqlAssembly, Action<MigrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(sqlAssembly);

        return Register((services, options) => services.AddDatabaseBespokeMigrationSet(setName, sqlAssembly, options), configure);
    }

    /// <summary>Adds a named set over a caller-built source, such as an <see cref="EmbeddedResourceMigrationBroker"/> with a per-dialect prefix.</summary>
    /// <param name="setName">The set name.</param>
    /// <param name="brokerFactory">Builds the set's migration source.</param>
    /// <param name="configure">An optional hook to override the set's <see cref="MigrationOptions"/>.</param>
    public MigratorSetsBuilder Add(string setName, Func<IServiceProvider, IMigrationBroker> brokerFactory, Action<MigrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(brokerFactory);

        return Register((services, options) => services.AddDatabaseBespokeMigrationSet(setName, brokerFactory, options), configure);
    }

    /// <summary>Runs <paramref name="sets"/> against a fresh builder and registers what it added, every set on <paramref name="provider"/>.</summary>
    /// <param name="services">The harness's service collection.</param>
    /// <param name="provider">The harness's database provider.</param>
    /// <param name="sets">Adds the sets.</param>
    /// <exception cref="InvalidOperationException">The callback added no set.</exception>
    internal static void Register(IServiceCollection services, DatabaseProvider provider, Action<MigratorSetsBuilder> sets)
    {
        var builder = new MigratorSetsBuilder();
        sets(builder);
        if (builder._registrations.Count == 0)
            throw new InvalidOperationException("The migrator harness needs at least one set: call AddDefault or Add.");

        foreach (var registration in builder._registrations)
            registration(services, provider);
    }

    /// <summary>Records one set's registration, with the harness's dialect applied before the caller's hook.</summary>
    /// <param name="registration">Registers the set with its final configure hook.</param>
    /// <param name="configure">The caller's hook, or null.</param>
    private MigratorSetsBuilder Register(Action<IServiceCollection, Action<MigrationOptions>> registration, Action<MigrationOptions>? configure)
    {
        _registrations.Add((services, provider) => registration(services, options =>
        {
            options.Provider = provider;
            configure?.Invoke(options);
        }));
        return this;
    }
}
