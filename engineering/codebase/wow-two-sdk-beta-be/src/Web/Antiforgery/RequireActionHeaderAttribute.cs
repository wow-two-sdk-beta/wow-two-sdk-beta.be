using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Web.Antiforgery;

/// <summary>Refuses a request whose action header does not name <paramref name="action"/>, before the body binds.</summary>
/// <remarks>
///   - a custom header is not CORS-safelisted, so a cross-origin page cannot send it without a preflight the API refuses;
///     the guard blocks cross-site cookie writes without trusting a body token
///   - the header name comes from <see cref="ActionHeaderOptions"/> (<see cref="ActionHeaderServiceCollectionExtensions.AddActionHeaderGuard"/>),
///     else <c>X-Requested-Action</c>
///   - a refused request gets a 400 validation problem through the SDK error path; its <c>errors[0].code</c> is
///     <see cref="ErrorCode"/> and <c>errors[0].property</c> the header name
/// </remarks>
/// <param name="action">The action the header must name exactly, such as <c>deploy</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireActionHeaderAttribute(string action) : Attribute, IResourceFilter
{
    /// <summary>The field-error code a refused request carries.</summary>
    public const string ErrorCode = "ActionHeaderRequired";

    private static readonly ActionHeaderOptions DefaultOptions = new();

    /// <summary>Gets the action the header must name.</summary>
    public string Action { get; } = string.IsNullOrWhiteSpace(action)
        ? throw new ArgumentException("An action name is required.", nameof(action))
        : action;

    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var http = context.HttpContext;
        var headerName = (http.RequestServices.GetService<ActionHeaderOptions>() ?? DefaultOptions).HeaderName;
        if (http.Request.Headers[headerName] == Action)
            return;

        var failure = new FieldError
        {
            Property = headerName,
            Code = ErrorCode,
            Message = $"An explicit {Action} action is required in the {headerName} header.",
            Params = new Dictionary<string, object>(StringComparer.Ordinal) { ["action"] = Action },
        };
        var problem = AppErrorProblemDetailsFactory.Resolve(http).Create(ValidationError.From([failure]), http);
        context.Result = new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
