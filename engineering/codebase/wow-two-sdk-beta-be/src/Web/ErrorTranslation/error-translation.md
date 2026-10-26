# Web.ErrorTranslation

Translated error messages, a part of the validation module. Off by default: every message renders as authored until
`Validation:Translation:Enabled` is set. The default error mappers apply it to problem details, field errors and
validation advisories.

## Enable

Code sets the application's defaults; the host section has the last word and reloads live.

```csharp
builder.Services.ConfigureValidation(o => o.Translation.Enabled = true);            // any host
builder.AddApiDefaults(o => o.Validation = v => v.Translation.SupportedCultures.Add("ru"));  // AddApiDefaults hosts
```

```jsonc
// appsettings.json — switches it on with no code change
"Validation": {
  "Translation": {
    "Enabled": true,
    "DefaultCulture": "en",                  // requests for it keep the authored text
    "SupportedCultures": [ "en", "ru", "uz" ],
    "Messages": {
      "ru": {
        "NotFound": "Не найдено.",                       // error type → top-level message
        "OrderMissing": "Заказ {orderId} не найден.",    // AppError metadata "messageKey"
        "NotEmptyValidator": "Заполните поле «{PropertyName}».",  // field-error code
        "DuplicateEmail": "Этот email уже занят."        // identity code
      }
    }
  }
}
```

## Lookup

| Message | Order |
|---|---|
| top-level (`AppError`) | catalog `messageKey` → package text for `messageKey` → catalog error type → built-in type text (`ru`, `uz`) → authored |
| field (`FieldError`) | catalog code → package text for the code → FluentValidation language pack → authored |

- Culture: the request-localization feature when that middleware ran, else `Accept-Language`, else the default culture.
- `SupportedCultures` empty accepts any culture a translation exists for; `uz` picks FluentValidation's Latin pack.
- Placeholders `{Name}` / `{Name:format}` fill from `FieldError.Params` or `AppError.Metadata`; a template naming a
  missing argument falls back to the authored message.
- Plurals use ICU syntax with CLDR rules (`en`/`uz` one·other, `ru` one·few·many·other):
  `"{Count, plural, =0 {нет заказов} one {# заказ} few {# заказа} many {# заказов} other {# заказа}}"`.
- `PseudoLocalization: true` accents and brackets every error message (`[!! Ñöţ ƒöûñđ !!]`) to spot untranslated text.
- `TranslateByErrorType: false` keeps specific authored messages unless a `messageKey` matches.
- The `Validation:Translation` section reloads live with the configuration.
- Products translate their own strings with `IErrorTranslationService.TryTranslate(context, key, arguments, out message)`.

## Package texts

A package ships the texts for the error codes it emits; core holds no other domain's texts.

```csharp
// in the package's registration — safe to call from each entry point; the same dictionary registers once
services.AddErrorTexts("ru", FileStorageErrorTextConstants.Ru)
        .AddErrorTexts("uz", FileStorageErrorTextConstants.Uz);   // IReadOnlyDictionary<string, string>: code → template
```

- Each call registers an `ErrorTextSpec` (culture + code → template); a neutral culture serves its sub-cultures.
- The host catalog wins over every package; a later package wins over an earlier one for the same code.
- The identity texts (`ru`, `uz`) seed the package texts while Identity ships in core; they move to Identity's own
  contract with it and register through `AddErrorTexts`.

## Response models

```csharp
return Results.Json(error.ToApiFailure<OrderDto>(http));              // ApiResponse<OrderDto>.Failure, translated
return Results.Problem(…) / throw identity.ToValidationError().ToException();   // identity codes → translatable field errors
```

- `ToApiFailure<T>` works for any response model: status from `IErrorHttpStatusCodeMapper`, message from `IErrorMessageMapper`.
- `IdentityResult.ToValidationError()` keeps each identity code and its values (`MinLength`, `Email`, `RoleName`, …).
