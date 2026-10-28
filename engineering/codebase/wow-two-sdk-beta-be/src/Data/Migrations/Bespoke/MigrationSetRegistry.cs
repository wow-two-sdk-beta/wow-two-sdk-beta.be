using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Marks one registered migration set, so the registry can list the sets in registration order.</summary>
/// <param name="Name">The set name.</param>
internal sealed record MigrationSetRegistration(string Name);

/// <summary>Resolves each set's services: the default set's unkeyed registrations, a named set's keyed ones.</summary>
internal sealed class MigrationSetRegistry : IMigrationSetRegistry
{
    private readonly IServiceProvider _services;

    /// <summary>Lists the registered sets and rejects two sets sharing a history table or an owned schema.</summary>
    /// <param name="services">The root provider the sets resolve from.</param>
    /// <param name="registrations">One marker per registered set, in registration order.</param>
    /// <exception cref="InvalidOperationException">Two sets share one history table or claim one schema, or the sets' dependencies form a cycle.</exception>
    public MigrationSetRegistry(IServiceProvider services, IEnumerable<MigrationSetRegistration> registrations)
    {
        _services = services;
        Names = registrations.Select(registration => registration.Name).Distinct(StringComparer.Ordinal).ToList();
        Validate();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; }

    /// <inheritdoc />
    public bool Contains(string setName) => Names.Contains(setName, StringComparer.Ordinal);

    /// <inheritdoc />
    public IMigrationRunnerService GetRunner(string setName) => Resolve<IMigrationRunnerService>(setName);

    /// <inheritdoc />
    public MigrationOptions GetOptions(string setName) => Resolve<MigrationOptions>(setName);

    /// <inheritdoc />
    public IMigrationBroker GetBroker(string setName) => Resolve<IMigrationBroker>(setName);

    /// <inheritdoc />
    public IMigrationDialect GetDialect(string setName) => Resolve<IMigrationDialect>(setName);

    private T Resolve<T>(string setName) where T : notnull
    {
        if (!Contains(setName))
            throw new KeyNotFoundException($"No migration set named '{setName}' is registered; registered: {string.Join(", ", Names)}.");

        return setName == MigrationConstants.DefaultSetName
            ? _services.GetRequiredService<T>()
            : _services.GetRequiredKeyedService<T>(setName);
    }

    private void Validate()
    {
        var histories = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownedSchemas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            var options = GetOptions(name);
            var history = GetDialect(name).QualifyHistoryTable(options.SchemaName, options.TableName);
            if (!histories.TryAdd(history, name))
            {
                throw new InvalidOperationException(
                    $"Migration sets '{histories[history]}' and '{name}' share the history table {history}; give each set its own TableName.");
            }

            if (options.SchemaOwner is not null && !ownedSchemas.TryAdd(options.SchemaName, name))
            {
                throw new InvalidOperationException(
                    $"Migration sets '{ownedSchemas[options.SchemaName]}' and '{name}' both claim schema '{options.SchemaName}'; one schema has one owning set.");
            }

            if (options.Dependencies.Any(dependency => dependency.Set == name))
                throw new InvalidOperationException($"Migration set '{name}' depends on itself.");
        }

        // Fails on a dependency cycle, so a host never boots with an order no run can follow.
        MigrationSetOrder.Sort(this, Names);
    }
}
