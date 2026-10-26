using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Provides registration for bounded child-process clients.</summary>
public static class ProcessClientServiceCollectionExtensions
{
    /// <summary>Registers the default <see cref="IProcessClient"/> singleton.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the time limit, output caps, inherited variables and failure parser; given the root services, so
    /// it can read configuration when the client is first resolved.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddProcessClient(
        this IServiceCollection services, Action<ProcessClientOptions, IServiceProvider>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddOptions(services, Options.DefaultName, configure);
        services.TryAddSingleton<IProcessClient>(provider => Create(provider, Options.DefaultName));
        return services;
    }

    /// <summary>Registers a keyed <see cref="IProcessClient"/> singleton, for a host running several tools under different bounds;
    /// inject it with <c>[FromKeyedServices(name)]</c>.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="name">The client's key and options name.</param>
    /// <param name="configure">Sets the time limit, output caps, inherited variables and failure parser.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddProcessClient(
        this IServiceCollection services, string name, Action<ProcessClientOptions, IServiceProvider>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        AddOptions(services, name, configure);
        services.TryAddKeyedSingleton<IProcessClient>(name, (provider, _) => Create(provider, name));
        return services;
    }

    private static void AddOptions(
        IServiceCollection services, string name, Action<ProcessClientOptions, IServiceProvider>? configure)
    {
        services.TryAddSingleton(TimeProvider.System);
        OptionsBuilder<ProcessClientOptions> options = services.AddOptions<ProcessClientOptions>(name);
        if (configure is not null)
        {
            options.Configure<IServiceProvider>((value, provider) => configure(value, provider));
        }

        options
            .Validate(value => value.Timeout > TimeSpan.Zero, "A process client's timeout must be positive.")
            .Validate(value => value.MaxOutputBytes > 0 && value.MaxErrorBytes > 0, "A process client's output caps must be positive.")
            .ValidateOnStart();
    }

    private static ProcessClient Create(IServiceProvider provider, string name) => new(
        provider.GetRequiredService<IOptionsMonitor<ProcessClientOptions>>().Get(name),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILogger<ProcessClient>>());
}
