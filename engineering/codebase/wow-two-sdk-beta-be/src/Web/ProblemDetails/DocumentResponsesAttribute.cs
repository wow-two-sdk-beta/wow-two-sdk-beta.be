using Microsoft.AspNetCore.Http.Metadata;

namespace WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;

/// <summary>
/// Marks an endpoint whose error responses are documents it writes itself, such as a public site page answering its
/// own HTML 404. The HTTP ProblemDetails boundary keeps what the endpoint writes: the MVC result filter leaves its
/// <c>4xx</c>/<c>5xx</c> results as they are, and status-code pages leave its empty error statuses empty. Apply it to
/// a controller, an action or a minimal API handler, or call
/// <see cref="DocumentResponsesExtensions.WithDocumentResponses{TBuilder}(TBuilder)"/> on an endpoint, a route group
/// or <c>MapControllers()</c>. An unhandled exception on a marked endpoint still answers ProblemDetails.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DocumentResponsesAttribute : Attribute, ISkipStatusCodePagesMetadata
{
}
