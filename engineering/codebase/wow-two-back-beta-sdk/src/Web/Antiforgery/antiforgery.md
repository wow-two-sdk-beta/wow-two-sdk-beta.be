# SPA antiforgery

> Cross-site request forgery protection for single-page apps that authenticate with cookies.

```csharp
builder.Services.AddSpaAntiforgery(options => options.ExemptPathPrefixes.Add("/api/billing/webhook"));

app.UseApiDefaults(useIdentity: pipeline =>
{
    pipeline.UseAuthentication();
    pipeline.UseAuthorization();
    pipeline.UseSpaAntiforgery();   // after authentication: tokens bind to the signed-in user
});
```

```ts
// Client: echo the readable cookie on every unsafe request.
const token = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]+)/)?.[1];
await fetch("/api/codes", { method: "POST", headers: { "X-XSRF-TOKEN": decodeURIComponent(token ?? "") }, body });
```

- Every safe request (`GET`, `HEAD`, `OPTIONS`, `TRACE`) refreshes the readable `XSRF-TOKEN` cookie.
- Unsafe requests that carry cookies must echo it in `X-XSRF-TOKEN`; otherwise they get a 400 problem.
- Requests without cookies pass: a browser cannot forge them, so bearer and server calls are unaffected.
- Exempt only callbacks that prove themselves another way, such as a signed payment webhook.
- Tokens rotate with sign-in; refresh the page state with a `GET` after authentication changes.
- Tests: `host.CreateSpaClient("/health")` keeps cookies and echoes tokens as the browser client does.

## Action header guard

> A write must name its action in a custom header, so a cross-site page cannot send it without a refused preflight.

```csharp
builder.Services.AddActionHeaderGuard(options => options.HeaderName = "X-Console-Action"); // default X-Requested-Action

[HttpPost("deployments")]
[RequireActionHeader("deploy")]          // resource filter: runs before the body binds
public Task<IActionResult> Start(DeploymentRequest request, CancellationToken ct) => ...;
```

```ts
await fetch("/api/deployments", { method: "POST", headers: { "X-Console-Action": "deploy" }, body });
```

- The header must equal the action exactly (ordinal); a missing or different value gets a 400.
- The 400 goes through `IAppErrorProblemDetailsFactory`: `code` `Validation`, `errors[0].code` `ActionHeaderRequired`, `errors[0].property` the header name.
- Host section `Web:ActionHeader` overrides the header name after code configuration.
