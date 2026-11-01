using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Reads the boot's validation failures into <c>{code, setting, message}</c> findings, without a second copy of any rule.</summary>
/// <remarks>
///   - a named rule (<see cref="SettingRuleExtensions"/>) carries its code and setting
///   - a data annotation names its member; its code is the attribute that produced the message (<c>Required</c> → <c>required</c>)
///   - any other failure reports code <c>invalid</c> on the record's section, the message as written
///   - a binding failure reports code <c>type</c> on the key it names, never the value
/// </remarks>
internal static partial class SettingsFailureReader
{
    /// <summary>Reads every failure of one record's validation.</summary>
    /// <param name="type">The record type.</param>
    /// <param name="section">The record's section, or null when code fills it.</param>
    /// <param name="exception">The validation failure.</param>
    public static IEnumerable<ConfigCheckInvalid> Read(Type type, string? section, OptionsValidationException exception)
    {
        var prefix = section ?? type.Name;
        foreach (var failure in exception.Failures)
            yield return Read(type, prefix, failure);
    }

    /// <summary>Reads a binding failure: a value that does not convert, or a key the binder refuses.</summary>
    /// <param name="type">The record type.</param>
    /// <param name="section">The record's section, or null.</param>
    /// <param name="exception">The binder's exception.</param>
    public static ConfigCheckInvalid ReadBinding(Type type, string? section, Exception exception)
    {
        var prefix = section ?? type.Name;
        if (exception.Message.Contains("ErrorOnUnknownConfiguration", StringComparison.Ordinal))
            return Finding("unknown-key", prefix, $"a key under the section binds to no member of {type.Name}");

        var conversion = Conversion().Match(exception.Message);
        return conversion.Success
            ? Finding("type", conversion.Groups["path"].Value, $"the value does not convert to {conversion.Groups["type"].Value}")
            : Finding("bind", prefix, $"the section does not bind to {type.Name} ({exception.GetType().Name})");
    }

    private static ConfigCheckInvalid Read(Type type, string prefix, string failure)
    {
        if (SettingRuleFailure.TryParse(failure, out var code, out var setting, out var message))
            return Finding(code, Join(prefix, setting), message);

        if (DataAnnotation().Match(failure) is { Success: true } annotation)
        {
            string[] path = [.. annotation.Groups["qualified"].Value.Split('.').Skip(1)];
            var member = annotation.Groups["members"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var error = annotation.Groups["error"].Value;
            string[] full = member is null ? path : [.. path, member];
            return Finding(AnnotationCode(type, path, member, error), Join(prefix, string.Join(':', full)), error);
        }

        if (MemberFailure().Match(failure) is { Success: true } memberFailure)
        {
            var path = memberFailure.Groups["member"].Value.Split('.');
            var error = memberFailure.Groups["error"].Value;
            return Finding(AnnotationCode(type, path[..^1], path[^1], error), Join(prefix, string.Join(':', path)), error);
        }

        return Finding("invalid", prefix, failure);
    }

    private static ConfigCheckInvalid Finding(string code, string setting, string message) =>
        new() { Code = code, Setting = setting, Message = message };

    private static string Join(string prefix, string relative) => relative.Length == 0 ? prefix : $"{prefix}:{relative}";

    /// <summary>Names the attribute whose message the failure carries, kebab-case without <c>Attribute</c>; <c>invalid</c> when none matches.</summary>
    private static string AnnotationCode(Type type, IReadOnlyList<string> path, string? member, string error)
    {
        if (member is null)
            return "invalid";

        var owner = type;
        foreach (var segment in path)
        {
            owner = Property(owner, segment)?.PropertyType;
            if (owner is null)
                return "invalid";
        }

        var property = Property(owner, member);
        if (property is null)
            return "invalid";

        var name = property.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? member;
        foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>(inherit: true))
        {
            string? formatted;
            try
            {
                formatted = attribute.FormatErrorMessage(name);
            }
            catch (InvalidOperationException)
            {
                formatted = null;
            }

            if (string.Equals(formatted, error, StringComparison.Ordinal))
                return Kebab(attribute.GetType().Name);
        }

        return "invalid";
    }

    private static PropertyInfo? Property(Type type, string name) =>
        (Nullable.GetUnderlyingType(type) ?? type).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

    private static string Kebab(string attributeName)
    {
        var name = attributeName.EndsWith("Attribute", StringComparison.Ordinal) ? attributeName[..^"Attribute".Length] : attributeName;
        return KebabBoundary().Replace(name, "-$1").ToLowerInvariant();
    }

    [GeneratedRegex(@"^DataAnnotation validation failed for '(?<qualified>[^']*)' members: '(?<members>[^']*)' with the error: '(?<error>.*)'\.$", RegexOptions.Singleline)]
    private static partial Regex DataAnnotation();

    [GeneratedRegex(@"^(?:Property )?(?<member>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*): (?<error>.+)$", RegexOptions.Singleline)]
    private static partial Regex MemberFailure();

    [GeneratedRegex(@"at '(?<path>[^']+)' to type '(?<type>[^']+)'")]
    private static partial Regex Conversion();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex KebabBoundary();
}
