using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.BackgroundServices;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core;

/// <summary>Provides registration for periodic jobs: work the host runs on an interval, each pass in a fresh DI scope.</summary>
public static class PeriodicJobServiceCollectionExtensions
{
    /// <summary>Registers a periodic job that resolves <typeparamref name="TService"/> from each pass's scope and runs <paramref name="run"/> on it.</summary>
    /// <typeparam name="TService">The registered service doing the work; resolved per pass, so a scoped service gets a fresh instance.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="name">The job's unique name; it names the options and tags logs and metrics.</param>
    /// <param name="run">The pass.</param>
    /// <param name="configure">Sets the interval, cadence, timeout and failure logging; given the root services, so it can read configuration when the host starts.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">A job with the same name is already registered.</exception>
    public static IServiceCollection AddPeriodicJob<TService>(
        this IServiceCollection services,
        string name,
        Func<TService, CancellationToken, Task> run,
        Action<PeriodicJobOptions, IServiceProvider>? configure = null)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(run);
        return services.AddPeriodicJob(
            name,
            (provider, cancellationToken) => run(provider.GetRequiredService<TService>(), cancellationToken),
            configure);
    }

    /// <summary>Registers a periodic job that resolves <typeparamref name="TService"/> from each pass's scope and runs <paramref name="run"/> on it.</summary>
    /// <typeparam name="TService">The registered service doing the work; resolved per pass, so a scoped service gets a fresh instance.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="name">The job's unique name; it names the options and tags logs and metrics.</param>
    /// <param name="run">The pass.</param>
    /// <param name="configure">Sets the interval, cadence, timeout and failure logging.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">A job with the same name is already registered.</exception>
    public static IServiceCollection AddPeriodicJob<TService>(
        this IServiceCollection services,
        string name,
        Func<TService, CancellationToken, Task> run,
        Action<PeriodicJobOptions> configure)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddPeriodicJob(name, run, (options, _) => configure(options));
    }

    /// <summary>Registers a periodic job whose pass receives its fresh scope's services.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="name">The job's unique name; it names the options and tags logs and metrics.</param>
    /// <param name="run">The pass, given the pass scope's services.</param>
    /// <param name="configure">Sets the interval, cadence, timeout and failure logging; given the root services, so it can read configuration when the host starts.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">A job with the same name is already registered.</exception>
    public static IServiceCollection AddPeriodicJob(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, CancellationToken, Task> run,
        Action<PeriodicJobOptions, IServiceProvider>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(PeriodicJobName) && Equals(descriptor.ImplementationInstance, new PeriodicJobName(name))))
        {
            throw new InvalidOperationException($"A periodic job named '{name}' is already registered.");
        }

        services.AddSingleton(new PeriodicJobName(name));
        services.TryAddSingleton(TimeProvider.System);
        OptionsBuilder<PeriodicJobOptions> options = services.AddOptions<PeriodicJobOptions>(name);
        if (configure is not null)
        {
            options.Configure<IServiceProvider>((value, provider) => configure(value, provider));
        }

        options
            .Validate(value => value.InitialDelay >= TimeSpan.Zero, $"Periodic job '{name}': the initial delay cannot be negative.")
            .ValidateOnStart();
        services.AddSingleton<IHostedService>(provider => new PeriodicJobBackgroundService(
            name,
            run,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptionsMonitor<PeriodicJobOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PeriodicJobBackgroundService>>()));
        return services;
    }

    /// <summary>Holds a registered job's name, so a second registration under it fails at composition.</summary>
    /// <param name="Value">The job's name.</param>
    private sealed record PeriodicJobName(string Value);
}
