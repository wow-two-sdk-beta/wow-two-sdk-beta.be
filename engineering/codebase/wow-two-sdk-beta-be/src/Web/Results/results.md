# Web.Results

Maps application results to HTTP. No code lives in this folder: a `Web.Results` namespace would shadow `Microsoft.AspNetCore.Http.Results` across `Web.*`, so the pieces live in the folders below. The application layer stays HTTP-free: a failure carries an `AppError`, and the web layer resolves its status and body.

- **`ErrorMapping/IErrorHttpStatusCodeMapper`** — `ToStatusCode(AppError)`; the default `ErrorHttpStatusCodeMapper` maps `AppErrorType` (400 · 401 · 402 · 403 · 404 · 409 · 410 · 422 · 429 · 499 · 500 · 503 · 504). Register your own to override.
- **`ExceptionHandling/Extensions/ControllerProblemExtensions.ToProblem(AppError)`** — a controller renders a failure as SDK ProblemDetails through the registered `IAppErrorProblemDetailsFactory` (status, `code`, `errors[]`).
- **`ExceptionHandling/Extensions/ControllerResultExtensions.ToActionResult<T>(AppResult<T>)`** — the whole controller tail in one call: success → `200` with `ApiResponse<T>.Ok(data)`, failure → `ToProblem`. The overload taking `Func<ApiResponse<T>.Success, IActionResult>` picks another success response (`CreatedAtAction`, `AcceptedAtAction`) around the same envelope.

```csharp
public async Task<IActionResult> Get(string id, CancellationToken ct) =>
    this.ToActionResult(await sender.SendAsync(new GetItemQuery(id), ct));

public async Task<IActionResult> Start(StartRequest request, CancellationToken ct) =>
    this.ToActionResult(await sender.SendAsync(request.ToCommand(), ct),
        envelope => AcceptedAtAction(nameof(Get), new { id = envelope.Data.Id }, envelope));
```

- **`Contracts/ApiResponseErrorExtensions.ToApiFailure<T>(AppError, HttpContext)`** — the `ApiResponse<T>.Failure` shape for the same error, for typed clients.

See also: `Mediator.Result` (`AppResult<T>`), `Web.Contracts` (the success envelope), `Web.ErrorMapping`.
