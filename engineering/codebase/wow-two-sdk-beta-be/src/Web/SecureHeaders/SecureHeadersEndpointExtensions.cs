using Microsoft.AspNetCore.Builder;

namespace WoW.Two.Sdk.Beta.Be.Web.SecureHeaders;

/// <summary>Provides the endpoint conventions the secure-headers preset reads.</summary>
public static class SecureHeadersEndpointExtensions
{
    /// <summary>Lets other origins embed this endpoint's responses (<c>Cross-Origin-Resource-Policy: cross-origin</c>).</summary>
    /// <param name="builder">The endpoint or group to mark.</param>
    public static TBuilder AllowCrossOriginEmbedding<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new AllowCrossOriginEmbeddingAttribute());
    }

    /// <summary>Answers one secure header this endpoint's own way: sets it to <paramref name="value"/> after the preset,
    /// or removes it when the value is null.</summary>
    /// <param name="builder">The endpoint or group to mark.</param>
    /// <param name="name">The header to answer.</param>
    /// <param name="value">The value to send; null removes the header.</param>
    public static TBuilder OverrideSecureHeader<TBuilder>(this TBuilder builder, string name, string? value)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.WithMetadata(new SecureHeaderOverrideAttribute(name, value));
    }
}
