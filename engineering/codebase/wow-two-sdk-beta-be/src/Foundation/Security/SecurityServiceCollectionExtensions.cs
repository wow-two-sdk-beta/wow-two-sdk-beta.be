using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Registration helpers for envelope cryptography.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>Registers envelope cryptography — <see cref="IValueCipher"/> (value encryption), <see cref="ISealService"/> (KEK key management), and the default env-backed <see cref="IMasterKeyBroker"/>.</summary>
    /// <remarks>All singletons: the seal keeper must outlive a request to hold the unsealed key. Registration is <c>TryAdd</c>-based, so register a KMS/HSM-backed <see cref="IMasterKeyBroker"/> before this call to swap the key source. Call <see cref="ISealService.Unseal"/> at startup to load the key.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional override of the master-key env-var name.</param>
    public static IServiceCollection AddEnvelopeCryptography(
        this IServiceCollection services,
        Action<EnvelopeCryptographyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new EnvelopeCryptographyOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<IMasterKeyBroker, EnvironmentMasterKeyBroker>();
        services.TryAddSingleton<ISealService, SealService>();
        services.TryAddSingleton<IValueCipher, ValueCipher>();

        return services;
    }

    /// <summary>Registers the master key file as the <see cref="IMasterKeyBroker"/> — <see cref="FileMasterKeyBroker"/>,
    /// replacing the environment broker in either call order — with the <see cref="MasterKeyProofTracker"/> and
    /// <see cref="MasterKeyUnsealService"/> that read it per use.</summary>
    /// <remarks>Singletons. Pair with <see cref="AddEnvelopeCryptography"/> for <see cref="IValueCipher"/>; its
    /// <see cref="ISealService"/> loads the same file once. The file is read on every use, so the key may arrive or leave
    /// while the host runs.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The key file's path; the host section <c>EnvelopeCryptography:KeyFile</c> is applied after it.</param>
    public static IServiceCollection AddFileMasterKeyBroker(
        this IServiceCollection services,
        Action<FileMasterKeyBrokerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModuleOptions(
            FileMasterKeyBrokerOptions.SectionName,
            configure,
            builder => builder.Validate(
                o => !string.IsNullOrWhiteSpace(o.Path) && Path.IsPathFullyQualified(o.Path),
                "FileMasterKeyBrokerOptions.Path must be an absolute path."));

        services.Replace(ServiceDescriptor.Singleton<IMasterKeyBroker>(provider =>
            new FileMasterKeyBroker(provider.GetRequiredService<FileMasterKeyBrokerOptions>().Path)));
        services.TryAddSingleton<MasterKeyProofTracker>();
        services.TryAddSingleton<MasterKeyUnsealService>();

        return services;
    }
}
