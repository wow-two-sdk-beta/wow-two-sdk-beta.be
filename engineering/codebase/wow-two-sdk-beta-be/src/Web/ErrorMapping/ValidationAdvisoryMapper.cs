using Microsoft.AspNetCore.Http;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Trackers;

namespace WoW.Two.Sdk.Beta.Be.Web.ErrorMapping;

/// <summary>Maps the request's tracked validation advisories through the field message seam.</summary>
/// <param name="tracker">The request-scoped advisory tracker.</param>
/// <param name="messages">The field message mapper shared with error responses.</param>
public sealed class ValidationAdvisoryMapper(IValidationAdvisoryTracker tracker, IFieldErrorMessageMapper messages)
    : IValidationAdvisoryMapper
{
    /// <inheritdoc />
    public IReadOnlyList<FieldError> Map(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return tracker.Advisories
            .Select(advisory => advisory with { Message = messages.Map(advisory, context) })
            .ToArray();
    }
}
