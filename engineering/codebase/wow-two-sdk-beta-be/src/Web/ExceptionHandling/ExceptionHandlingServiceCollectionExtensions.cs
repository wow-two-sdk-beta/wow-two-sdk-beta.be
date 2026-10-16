using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Observability.Errors;
using WoW.Two.Sdk.Beta.Be.Web.ErrorMapping;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling;

/// <summary>Provides registration for mapping thrown exceptions to ProblemDetails responses.</summary>
public static class ExceptionHandlingServiceCollectionExtensions
{
    /// <summary>Adds the handler mapping a <see cref="Foundation.Validation.ValidationException"/> to a 400 ProblemDetails response.</summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddValidationExceptionHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddErrorHttpStatusMapping();
        services.TryAddSingleton<IAppErrorProblemDetailsFactory, AppErrorProblemDetailsFactory>();
        services.AddExceptionHandler<ValidationExceptionHandler>();

        return services;
    }

    /// <summary>Adds the full SDK exception-handling chain — validation, any <see cref="Foundation.Errors.AppException"/>, and a safe 500 fallback — plus the mapping and observability seams.</summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddAppExceptionHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddErrorHttpStatusMapping();
        services.TryAddSingleton<IAppErrorProblemDetailsFactory, AppErrorProblemDetailsFactory>();
        services.AddErrorRecordingService();
        services.AddExceptionMapping();

        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddExceptionHandler<UnhandledExceptionHandler>();

        return services;
    }
}
