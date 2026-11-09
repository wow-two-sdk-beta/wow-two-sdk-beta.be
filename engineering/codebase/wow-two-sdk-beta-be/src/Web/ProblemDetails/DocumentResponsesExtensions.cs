using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;

/// <summary>Marks endpoints whose error responses are documents they write themselves.</summary>
public static class DocumentResponsesExtensions
{
    /// <summary>
    /// Adds <see cref="DocumentResponsesAttribute"/> to the endpoints the builder produces, so the HTTP ProblemDetails
    /// boundary keeps the error responses they write instead of rewriting them as problem JSON.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type: an endpoint, a route group or controllers.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static TBuilder WithDocumentResponses<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DocumentResponsesAttribute());
    }

    /// <summary>Reports whether the request's endpoint writes its own error documents.</summary>
    /// <param name="context">The current request.</param>
    /// <returns><see langword="true"/> when the matched endpoint carries <see cref="DocumentResponsesAttribute"/>.</returns>
    internal static bool HasDocumentResponses(this HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<DocumentResponsesAttribute>() is not null;
}
