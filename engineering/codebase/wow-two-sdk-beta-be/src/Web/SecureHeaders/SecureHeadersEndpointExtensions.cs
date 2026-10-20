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
}
