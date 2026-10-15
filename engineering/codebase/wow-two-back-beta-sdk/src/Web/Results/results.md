# Web.Results

Maps application failure results to HTTP. The application layer stays HTTP-free: a failure carries an `AppError`, and the web layer resolves its status and body.

- **`ErrorMapping/IErrorHttpStatusCodeMapper`** — `ToStatusCode(AppError)`; the default `ErrorHttpStatusCodeMapper` maps `AppErrorType` (400 · 401 · 402 · 403 · 404 · 409 · 410 · 422 · 429 · 499 · 500 · 503 · 504). Register your own to override.
- **`ExceptionHandling/Extensions/ControllerProblemExtensions.ToProblem(AppError)`** — a controller renders a failure as SDK ProblemDetails through the registered `IAppErrorProblemDetailsFactory` (status, `code`, `errors[]`).
- **`Contracts/ApiResponseErrorExtensions.ToApiFailure<T>(AppError, HttpContext)`** — the `ApiResponse<T>.Failure` shape for the same error, for typed clients.

See also: `Mediator.Result` (`AppResult<T>`), `Web.Contracts` (the success envelope), `Web.ErrorMapping`.
