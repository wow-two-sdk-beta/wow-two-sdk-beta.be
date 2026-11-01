using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Options;

/// <summary>States a settings rule that names its setting and its rule code, so a failure reads the same at boot and in <c>config check</c>.</summary>
/// <remarks>
///   - a failure renders as <c>[{code}] {setting}: {message}</c>; <c>config check</c> reports it as <c>{code, setting, message}</c>,
///     the setting prefixed with the record's section
///   - use it for a rule across settings, such as a minimum above its maximum (<c>range</c>); data annotations already name their member
///   - keep <c>message</c> to the rule broken, never a secret value
/// </remarks>
/// <example>
/// <code>
/// services.AddValidatedSettings&lt;PasswordSettings&gt;(configuration, "Identity:Password", rules => rules
///     .ValidateSetting(nameof(PasswordSettings.MinLength), "range",
///         settings => settings.MinLength &lt;= settings.MaxLength,
///         settings => $"{settings.MinLength} exceeds MaxLength {settings.MaxLength}"));
/// </code>
/// </example>
public static class SettingRuleExtensions
{
    /// <summary>Adds a rule on <typeparamref name="T"/> whose failure names <paramref name="setting"/> and <paramref name="code"/>.</summary>
    /// <typeparam name="T">The settings record.</typeparam>
    /// <param name="builder">The record's options builder.</param>
    /// <param name="setting">The setting the rule reports, relative to the record's section, such as <c>MinLength</c> or <c>Methods:Password:IsEnabled</c>.</param>
    /// <param name="code">The rule's short kebab-case id, such as <c>range</c>.</param>
    /// <param name="isValid">Returns whether the bound record satisfies the rule.</param>
    /// <param name="message">States the rule broken for the bound record; never a secret value.</param>
    /// <returns>The builder, for chaining.</returns>
    public static OptionsBuilder<T> ValidateSetting<T>(
        this OptionsBuilder<T> builder, string setting, string code, Func<T, bool> isValid, Func<T, string> message)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(isValid);
        ArgumentNullException.ThrowIfNull(message);
        SettingRuleFailure.ThrowIfInvalid(setting, code);

        builder.Services.AddSingleton<IValidateOptions<T>>(new SettingRuleValidator<T>(builder.Name, setting, code, isValid, message));
        return builder;
    }

    /// <summary>Adds a rule on <typeparamref name="T"/> whose failure names <paramref name="setting"/> and <paramref name="code"/>.</summary>
    /// <typeparam name="T">The settings record.</typeparam>
    /// <param name="builder">The record's options builder.</param>
    /// <param name="setting">The setting the rule reports, relative to the record's section.</param>
    /// <param name="code">The rule's short kebab-case id, such as <c>required</c>.</param>
    /// <param name="isValid">Returns whether the bound record satisfies the rule.</param>
    /// <param name="message">States the rule broken; never a secret value.</param>
    /// <returns>The builder, for chaining.</returns>
    public static OptionsBuilder<T> ValidateSetting<T>(
        this OptionsBuilder<T> builder, string setting, string code, Func<T, bool> isValid, string message)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return builder.ValidateSetting(setting, code, isValid, _ => message);
    }
}

/// <summary>Runs one <see cref="SettingRuleExtensions.ValidateSetting{T}(OptionsBuilder{T}, string, string, Func{T, bool}, Func{T, string})"/> rule.</summary>
/// <typeparam name="T">The settings record.</typeparam>
internal sealed class SettingRuleValidator<T>(
    string? name, string setting, string code, Func<T, bool> isValid, Func<T, string> message) : IValidateOptions<T>
    where T : class
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? optionsName, T options)
    {
        if (name is not null && name != optionsName)
            return ValidateOptionsResult.Skip;

        return isValid(options)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(SettingRuleFailure.Format(code, setting, message(options)));
    }
}

/// <summary>Renders and reads a named rule's failure: <c>[{code}] {setting}: {message}</c>.</summary>
internal static partial class SettingRuleFailure
{
    /// <summary>Renders a failure.</summary>
    /// <param name="code">The rule code.</param>
    /// <param name="setting">The setting, relative to the record's section.</param>
    /// <param name="message">The rule broken.</param>
    public static string Format(string code, string setting, string message) => $"[{code}] {setting}: {message}";

    /// <summary>Reads a failure rendered by <see cref="Format"/>.</summary>
    /// <param name="failure">One validation failure.</param>
    /// <param name="code">The rule code.</param>
    /// <param name="setting">The setting, relative to the record's section.</param>
    /// <param name="message">The rule broken.</param>
    /// <returns>Whether the failure came from a named rule.</returns>
    public static bool TryParse(string failure, out string code, out string setting, out string message)
    {
        var match = Failure().Match(failure);
        (code, setting, message) = match.Success
            ? (match.Groups["code"].Value, match.Groups["setting"].Value, match.Groups["message"].Value)
            : ("", "", "");
        return match.Success;
    }

    /// <summary>Throws when the setting holds whitespace or the code is not kebab-case.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="code">The rule code.</param>
    public static void ThrowIfInvalid(string setting, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (setting.Any(char.IsWhiteSpace))
            throw new ArgumentException("A setting path holds no whitespace, such as MinLength or Methods:Password:IsEnabled.", nameof(setting));
        if (!Code().IsMatch(code))
            throw new ArgumentException("A rule code is short kebab-case, such as range or only-method-removed.", nameof(code));
    }

    [GeneratedRegex(@"^\[(?<code>[a-z0-9]+(?:-[a-z0-9]+)*)\] (?<setting>\S+): (?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex Failure();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Code();
}
