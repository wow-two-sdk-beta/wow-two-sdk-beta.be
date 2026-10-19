using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Web.DataProtection;

/// <summary>Provides registration for a persistent Data Protection key ring.</summary>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>Registers Data Protection with a stable application name and a key ring stored on disk.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the application name and key directory.</param>
    /// <remarks>
    ///   - outside a local run, deployed dev included, startup fails without a key directory: ephemeral keys void every cookie on restart
    ///   - authentication cookies, antiforgery tokens and guest capabilities stay valid across container replacement
    ///   - the key ring is stored unencrypted; restrict access to the volume
    /// </remarks>
    public static IServiceCollection AddPersistentDataProtection(
        this IServiceCollection services,
        Action<PersistentDataProtectionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddValidatedOptions(
            configure,
            static builder => builder
                .Validate(static options => !string.IsNullOrWhiteSpace(options.ApplicationName), "DataProtection: ApplicationName is required.")
                .Validate<IServiceProvider>(
                    static (options, provider) => !string.IsNullOrWhiteSpace(options.KeyDirectory) || LocalRunExtensions.IsLocalRun(provider),
                    "DataProtection: KeyDirectory is required outside a local run."));
        services.AddDataProtection();
        services.AddOptions<DataProtectionOptions>()
            .Configure<PersistentDataProtectionOptions>(static (protection, options) =>
                protection.ApplicationDiscriminator = options.ApplicationName);
        services.AddOptions<KeyManagementOptions>()
            .Configure<PersistentDataProtectionOptions, ILoggerFactory>(static (keys, options, loggers) =>
            {
                if (!string.IsNullOrWhiteSpace(options.KeyDirectory))
                    keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(options.KeyDirectory), loggers);
            });
        return services;
    }
}
