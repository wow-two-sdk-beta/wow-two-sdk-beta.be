# WoW.Two.Sdk.Beta.Be.Foundation.Errors

> The transport-agnostic failure model: `AppError` (value) + `AppErrorType` (kind) + `AppException` (throw form), the `AppErrorFactory` catalog, descriptive `ErrorNature`, and the extensible exception→`AppError` mapping seam. Consumed by results, the mediator behaviors, and the ProblemDetails handlers. The value types (`AppError` … `ErrorOrigin`, `ExceptionChainMapper`) ship in the BCL-only kernel `WoW2.Sdk.Beta.Be.Abstractions` ([`src/Abstractions/Foundation/Errors/`](../../Abstractions/abstractions.md)), same namespace; the mapping seam below stays in core.

## Model

- **`AppError`** — open `record` carrying `Type` (`AppErrorType`), `Message`, optional `Metadata`, and log-only `Origin`. Authored via `AppError.Of(...)`, `AppError.FromException(...)`, or the `AppErrorFactory` catalog (`AppErrorFactory.NotFound(...)`, `Conflict`, `Validation`, …). `Type` is the wire `code`.
- **`AppException`** — the throw form; *carries* an `AppError` (one source of truth). Bridge both ways: `error.Throw()` / `error.ToException()`.
- **`ErrorNature {Transient, Permanent, Defect}`** via `IErrorNatureMapper` (DI) — consumers derive retry/fallback/log level.

## Returning vs throwing

```csharp
// return (expected failure) — see Abstractions/Foundation/Results/results.md
public Result<User> GetUser(Guid id)
    => _repo.Find(id) is { } user ? user : AppErrorFactory.NotFound("User not found");

// throw (exceptional) — the mediator behavior / global handler turns it into ProblemDetails
AppErrorFactory.Conflict("Email already registered.").Throw();
```

## Mapping a caught exception → AppError

`IExceptionMapper.Map(Exception)` is the total facade the mediator behavior and global handlers use. The default `ExceptionMapper` unwraps an `AppException`, then walks the registered `IExceptionMappingRule` contributors **last-registered-first**, else falls back to `Unexpected`. The SDK ships `DbExceptionMappingRule` (Npgsql/EF → `AppError`), auto-wired by `AddPostgresPersistence`.

```csharp
// map an exception the SDK does not know — your rule shadows SDK rules for the same exception
public sealed class PaymentDeclinedRule : IExceptionMappingRule
{
    public AppError? TryMap(Exception ex)
        => ex is PaymentDeclinedException ? AppErrorFactory.PaymentRequired("Card declined.") : null;
}

builder.Services.AddExceptionMappingRule<PaymentDeclinedRule>();
```

## See also

- [Foundation.Results](../../Abstractions/Foundation/Results/results.md) — `Result` / `Result<T>` wrappers (kernel)
- [Web.ErrorMapping / Web.ExceptionHandling](../../Web/ExceptionHandling/) — `IErrorHttpStatusCodeMapper`,
  `IAppErrorProblemDetailsFactory` and the exception handlers
- [`engineering/research/errors/errors-architecture-investigation.md`](../../../../../research/errors/errors-architecture-investigation.md) — full design record
