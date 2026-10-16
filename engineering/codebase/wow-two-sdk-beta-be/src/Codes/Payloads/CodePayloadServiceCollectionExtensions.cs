using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Codes.Payloads.Exporters;
using WoW.Two.Sdk.Beta.Be.Codes.Payloads.Serializers;

namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads;

/// <summary>Extends service registration with scanner payload formats.</summary>
public static class CodePayloadServiceCollectionExtensions
{
    /// <summary>Registers stateless payload serializers and contact/calendar exporters.</summary>
    public static IServiceCollection AddCodePayloads(this IServiceCollection services)
    {
        services.TryAddSingleton<ICodePayloadSerializer, CodePayloadSerializer>();
        services.TryAddSingleton<IVCardExporter, VCardExporter>();
        services.TryAddSingleton<IFloatingCalendarEventExporter, FloatingCalendarEventExporter>();
        return services;
    }
}
