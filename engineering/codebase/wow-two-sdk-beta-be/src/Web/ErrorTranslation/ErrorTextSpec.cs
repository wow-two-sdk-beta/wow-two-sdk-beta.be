namespace WoW.Two.Sdk.Beta.Be.Web.ErrorTranslation;

/// <summary>
/// Declares one package's texts for the error codes it emits, in one culture — the seam that lets a domain package ship
/// its own error texts instead of core holding them. Register it with
/// <see cref="ErrorTranslationServiceCollectionExtensions.AddErrorTexts"/>; <see cref="ErrorTranslationService"/>
/// consults every registered spec after the host catalog and before FluentValidation's language pack.
/// </summary>
public sealed record ErrorTextSpec
{
    /// <summary>Gets the culture the texts are written in; a neutral name such as <c>ru</c> also serves its sub-cultures.</summary>
    public required string Culture { get; init; }

    /// <summary>Gets the message template per error code — a <c>FieldError.Code</c> or an <c>AppError</c> <c>messageKey</c>; <c>{Name}</c> placeholders fill from the error's values.</summary>
    public required IReadOnlyDictionary<string, string> Messages { get; init; }
}
