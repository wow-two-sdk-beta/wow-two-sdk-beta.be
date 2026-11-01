using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Options;

/// <summary>Names the recipe a settings record came through, which decides whether <c>config check</c> walks its section on its own.</summary>
/// <remarks>Lives beside the recipe, web-free, so <c>wow-migrate</c> links it with the rest of <c>Foundation/Options</c>.</remarks>
internal enum SettingsRecordKind
{
    /// <summary><c>AddValidatedSettings</c>: the service's own section, always walked for unknown keys.</summary>
    Settings,

    /// <summary><c>AddModuleOptions</c>: a module's section, walked only under a declared settings root.</summary>
    Module,

    /// <summary><c>AddValidatedOptions</c>: filled in code, no section.</summary>
    Options,
}

/// <summary>One record the options recipe registered: its type, its section and how to run its boot validation.</summary>
/// <param name="type">The record type.</param>
/// <param name="section">The configuration section it binds, or null when code fills it.</param>
/// <param name="kind">The recipe it came through.</param>
/// <param name="resolve">Resolves the record the way the boot's startup validation does, throwing its validation failure.</param>
internal sealed class SettingsRecord(Type type, string? section, SettingsRecordKind kind, Action<IServiceProvider> resolve)
{
    /// <summary>Gets the record type.</summary>
    public Type Type { get; } = type;

    /// <summary>Gets the configuration section it binds, or null when code fills it.</summary>
    public string? Section { get; } = section;

    /// <summary>Gets the section split on <c>:</c>; empty without a section.</summary>
    public string[] SectionSegments { get; } = section?.Split(':') ?? [];

    /// <summary>Gets the recipe the record came through.</summary>
    public SettingsRecordKind Kind { get; } = kind;

    /// <summary>Resolves the record as the boot does: <c>IOptionsMonitor&lt;T&gt;.Get</c> under the default name.</summary>
    /// <param name="services">The host's root provider.</param>
    public void Resolve(IServiceProvider services) => resolve(services);

    /// <summary>Creates the record for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="section">The section it binds, or null.</param>
    /// <param name="kind">The recipe it came through.</param>
    public static SettingsRecord For<T>(string? section, SettingsRecordKind kind)
        where T : class
        => new(typeof(T), section, kind, static services =>
            _ = services.GetRequiredService<IOptionsMonitor<T>>().Get(Microsoft.Extensions.Options.Options.DefaultName));
}
