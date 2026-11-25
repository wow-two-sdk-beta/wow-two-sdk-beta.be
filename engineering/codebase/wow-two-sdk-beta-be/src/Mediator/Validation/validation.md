# WoW.Two.Sdk.Beta.Be.Mediator.Validation

> Mediator validation checks requests before the handler and successful responses before returning them to the caller.

## Install

```
dotnet add package WoW2.Sdk.Beta.Be
```

## Usage

```csharp
builder.Services.AddFluentValidatorsFromAssemblies(typeof(Program).Assembly);
builder.Services.AddMediator(typeof(Program).Assembly);
builder.Services.AddMediatorValidatingInterceptor();
```

Input validators implement `IValidator<TRequest>`. The interceptor combines blocking field failures in registration order and throws one `ValidationException`. The mediator's exception mapper and the web problem-details mapping render the failure as HTTP 400.

Output validators implement `IValidator<TResponse>`, including the result wrapper for an `AppResult<T>` response. Failure results pass through. A blocking output rule returns a generic internal error through the exception mapper. Output values and field errors stay out of the HTTP response.

## Phase placement

Use the interceptor for target-free requests whose authored field validation belongs before the handler.

For a command that addresses an existing target, resolve the target and check ownership first. Run the request's SDK validator from the handler or shared application service only after those checks. The generic interceptor cannot establish that phase order because it always runs before the handler.

Keep persistence and other I/O out of static payload validators. Transition rules that need prior state belong on the resolved entity or a domain validator.

## Contract boundaries

- `IValidator<T>.Validate` is synchronous and returns `null` when valid.
- `IValidator<T>.Inspect` reports advisory failures without rejecting a write.
- FluentValidation supplies the default field-rule adapter; callers depend on the SDK interface.
- Validate copied or deserialized candidates where the application accepts them for use or persistence.
- Async validation, caller-selected rulesets, and target-resolution integration require separate explicit seams.
