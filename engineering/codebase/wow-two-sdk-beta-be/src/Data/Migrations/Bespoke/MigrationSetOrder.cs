namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Orders sets so every set runs after the registered sets it depends on, registration order breaking ties.</summary>
internal static class MigrationSetOrder
{
    /// <summary>Sorts the given sets topologically by their <see cref="MigrationOptions.Dependencies"/> on registered sets.</summary>
    /// <param name="registry">The registered sets.</param>
    /// <param name="names">The sets to order, a subset of the registry.</param>
    /// <returns>The sets in run order.</returns>
    /// <exception cref="InvalidOperationException">The dependencies form a cycle.</exception>
    public static IReadOnlyList<string> Sort(IMigrationSetRegistry registry, IEnumerable<string> names)
    {
        var selected = names.ToHashSet(StringComparer.Ordinal);
        var remaining = registry.Names.Where(selected.Contains).ToList();
        var ordered = new List<string>(remaining.Count);

        while (remaining.Count > 0)
        {
            // The earliest-registered set whose registered dependencies already ran goes next.
            var next = remaining.FirstOrDefault(name => registry.GetOptions(name).Dependencies
                .All(dependency => !remaining.Contains(dependency.Set, StringComparer.Ordinal) || dependency.Set == name));
            if (next is null)
                throw new InvalidOperationException($"Migration sets {string.Join(", ", remaining)} depend on each other in a cycle.");

            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }
}
