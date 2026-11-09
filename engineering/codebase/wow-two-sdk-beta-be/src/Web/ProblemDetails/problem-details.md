# ProblemDetails

*Last updated: 2026-11-09*

> RFC 9457 HTTP errors and trace context from `WoW2.Sdk.Beta.Be`.

```csharp
using WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;
builder.Services.AddHttpProblemDetails();
var app = builder.Build();
app.UseHttpProblemDetails();
```

`AddTraceAwareProblemDetails()` remains the lower-level registration.
The complete opt-in HTTP boundary adds the SDK exception chain, status pages and MVC result normalization.

## Document responses

A public site's document routes answer their own HTML error page; every other route keeps problem JSON.

```csharp
[DocumentResponses]                                   // a controller or an action
public sealed class JournalController : Controller { /* return a ContentResult with status 404 */ }

app.MapGroup("/journal").WithDocumentResponses();     // an endpoint, a route group or MapControllers()
```

- A marked endpoint keeps what it writes: the MVC result filter leaves its `4xx`/`5xx` results alone.
- Status-code pages skip it, so an empty `NotFound()` stays an empty 404.
- An unhandled exception there still answers problem JSON, with the usual trace ids.
- Its error statuses still carry `Cache-Control: no-store`.
- `[ApiController]` maps client errors itself; leave it off document controllers.

[API](HttpProblemDetails.spec.md) · [Contract](HttpProblemDetails.standard.md) ·
[ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling) ·
[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)
