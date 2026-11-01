using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Registers what a service adds to <c>config check</c> beyond its settings records: its settings roots and its stranding checks.</summary>
public static class ConfigCheckServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="TCheck"/>, run in a fresh scope by <c>config check</c> and at every boot.</summary>
    /// <typeparam name="TCheck">The service's stranding check.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>Call it in the <c>Add{Domain}</c> that binds the setting the check guards; registering the same type twice adds it once.</remarks>
    public static IServiceCollection AddStrandingCheck<TCheck>(this IServiceCollection services)
        where TCheck : class, IStrandingCheck
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStrandingCheck, TCheck>());
        return services;
    }

    /// <summary>Declares <paramref name="section"/> the service's own root: <c>config check</c> refuses every key under it that no settings record binds.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="section">The root section, such as <c>Identity</c>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    ///   - a key under a section no record binds, such as a retired <c>Identity:SignUp:Enabled</c>, is found only under a declared root
    ///   - every section bound through <c>AddValidatedSettings</c> is walked without one; module sections under the root are walked too
    ///   - never declare a section the framework or another component owns, such as <c>Logging</c> or <c>AllowedHosts</c>
    /// </remarks>
    public static IServiceCollection AddSettingsRoot(this IServiceCollection services, string section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        services.AddSingleton(new SettingsRoot { Section = section });
        return services;
    }
}
