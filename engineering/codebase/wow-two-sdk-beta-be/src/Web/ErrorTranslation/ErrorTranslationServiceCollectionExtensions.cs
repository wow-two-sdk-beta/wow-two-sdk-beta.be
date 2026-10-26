using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;

namespace WoW.Two.Sdk.Beta.Be.Web.ErrorTranslation;

/// <summary>Registers error translation over the validation module's options.</summary>
public static class ErrorTranslationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IErrorTranslationService"/>, reading <see cref="ValidationOptions.Translation"/> live. Enable it with
    /// <c>ConfigureValidation(o => o.Translation.Enabled = true)</c> or <c>Validation:Translation:Enabled</c> in host configuration;
    /// the error-mapping registration calls this already.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddErrorTranslation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ConfigureValidation();
        services.TryAddSingleton<IErrorTranslationService, ErrorTranslationService>();
        return services;
    }

    /// <summary>
    /// Contributes a package's own texts for the error codes it emits in <paramref name="culture"/>, so core needs no other
    /// domain's texts. The host catalog (<see cref="ValidationTranslationOptions.Messages"/>) wins over every contribution,
    /// and a later contribution wins over an earlier one for the same code. Registering the same dictionary for the same
    /// culture again keeps one registration, so a package may call this from each of its entry points.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="culture">The culture the texts are written in, e.g. <c>ru</c> or <c>uz</c>.</param>
    /// <param name="messages">The message template per error code.</param>
    public static IServiceCollection AddErrorTexts(this IServiceCollection services, string culture, IReadOnlyDictionary<string, string> messages)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(messages);

        var registered = services.Any(descriptor => descriptor.ServiceType == typeof(ErrorTextSpec)
            && !descriptor.IsKeyedService
            && descriptor.ImplementationInstance is ErrorTextSpec spec
            && string.Equals(spec.Culture, culture, StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(spec.Messages, messages));
        if (!registered)
            services.AddSingleton(new ErrorTextSpec { Culture = culture, Messages = messages });

        return services;
    }
}
