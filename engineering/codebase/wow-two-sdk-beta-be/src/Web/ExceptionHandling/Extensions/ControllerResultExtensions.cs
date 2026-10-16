using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

/// <summary>Extends controllers with one rendering of an <see cref="AppResult{TSuccess}"/>: the success envelope or SDK problem details.</summary>
public static class ControllerResultExtensions
{
    /// <summary>Renders <paramref name="result"/> as <c>200</c> with <see cref="ApiResponse{T}.Ok"/> on success, or as SDK problem details on failure.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="controller">Controller handling the request.</param>
    /// <param name="result">The application outcome to render.</param>
    /// <remarks>A failure renders through <see cref="ControllerProblemExtensions.ToProblem"/>, so the registered error mappers decide its status and body.</remarks>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, AppResult<T> result)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(controller);

        return controller.ToActionResult(result, envelope => controller.Ok(envelope));
    }

    /// <summary>Renders <paramref name="result"/> through <paramref name="onSuccess"/> with the success envelope, or as SDK problem details on failure.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="controller">Controller handling the request.</param>
    /// <param name="result">The application outcome to render.</param>
    /// <param name="onSuccess">Chooses the success response for the built envelope, such as <c>envelope =&gt; CreatedAtAction(nameof(Get), new { id = envelope.Data.Id }, envelope)</c>.</param>
    public static IActionResult ToActionResult<T>(
        this ControllerBase controller,
        AppResult<T> result,
        Func<ApiResponse<T>.Success, IActionResult> onSuccess)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.Match<IActionResult>(
            success => onSuccess(ApiResponse<T>.Ok(success.Data)),
            failure => controller.ToProblem(failure.Error));
    }
}
