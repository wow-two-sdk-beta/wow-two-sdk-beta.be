using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Finds the keys under the service's own sections that no settings record binds.</summary>
/// <remarks>
///   - walks every declared root and every section bound through <c>AddValidatedSettings</c>, leaf key by leaf key
///   - a key is bound when a record's section covers it and the record's members, followed segment by segment, reach a value:
///     a property (or its <see cref="ConfigurationKeyNameAttribute"/> name), a dictionary entry, a collection index
///   - a key under a scalar member, past a collection element's value or under no member is unknown
///   - names keys only, never values
/// </remarks>
internal static class SettingsKeyWalker
{
    /// <summary>Lists the unknown leaf keys, sorted.</summary>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="records">Every settings record.</param>
    /// <param name="roots">The declared settings roots.</param>
    public static IReadOnlyList<string> FindUnknown(IConfiguration configuration, IReadOnlyList<SettingsRecord> records, IEnumerable<string> roots)
    {
        var bound = records.Where(record => record.Section is not null).ToList();
        var scopes = roots
            .Concat(bound.Where(record => record.Kind == SettingsRecordKind.Settings).Select(record => record.Section!))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var unknown = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in scopes)
        {
            foreach (var (key, value) in configuration.GetSection(scope).AsEnumerable())
            {
                if (value is not null && !IsBound(key.Split(':'), value, bound))
                    unknown.Add(key);
            }
        }

        return [.. unknown];
    }

    private static bool IsBound(string[] segments, string value, List<SettingsRecord> bound)
    {
        var covering = bound.Where(record => Covers(record.SectionSegments, segments)).ToList();
        return covering.Any(record => Accepts(record.Type, segments, record.SectionSegments.Length, value));
    }

    private static bool Covers(string[] section, string[] key)
    {
        if (section.Length > key.Length)
            return false;
        for (var index = 0; index < section.Length; index++)
        {
            if (!string.Equals(section[index], key[index], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool Accepts(Type type, string[] segments, int index, string value)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(object) || typeof(IConfiguration).IsAssignableFrom(type))
            return true;

        var scalar = IsScalar(type);
        if (index == segments.Length)
            return scalar || value.Length == 0;
        if (scalar)
            return false;

        if (DictionaryValue(type) is { } entry)
            return Accepts(entry, segments, index + 1, value);
        if (CollectionElement(type) is { } element)
            return Accepts(element, segments, index + 1, value);

        var property = FindProperty(type, segments[index]);
        return property is not null && Accepts(property.PropertyType, segments, index + 1, value);
    }

    private static PropertyInfo? FindProperty(Type type, string key) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            .FirstOrDefault(property => string.Equals(
                property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name, key, StringComparison.OrdinalIgnoreCase));

    private static bool IsScalar(Type type) =>
        type == typeof(string) || type == typeof(byte[]) || type.IsPrimitive || type.IsEnum
        || TypeDescriptor.GetConverter(type).CanConvertFrom(typeof(string));

    private static Type? DictionaryValue(Type type) =>
        Generic(type, typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>)) is { } dictionary ? dictionary.GetGenericArguments()[1] : null;

    private static Type? CollectionElement(Type type) =>
        type.IsArray ? type.GetElementType()
        : Generic(type, typeof(IEnumerable<>)) is { } enumerable ? enumerable.GetGenericArguments()[0]
        : null;

    private static Type? Generic(Type type, params Type[] definitions)
    {
        if (type.IsGenericType && definitions.Contains(type.GetGenericTypeDefinition()))
            return type;

        return type.GetInterfaces().FirstOrDefault(candidate => candidate.IsGenericType && definitions.Contains(candidate.GetGenericTypeDefinition()));
    }
}
