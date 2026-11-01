# Meta — `AddApiDefaults` / `UseApiDefaults`

> The one-import boot floor. Namespace `WoW.Two.Sdk.Beta.Be` (root) — one `using` lights it up.

```csharp
using WoW.Two.Sdk.Beta.Be;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults(o =>
{
    o.ValidatorAssemblies.Add(typeof(Program).Assembly);   // optional
    o.CorsOrigins.Add("https://app.example.com");          // optional
});

var app = builder.Build();
app.UseApiDefaults(pipeline =>
{
    pipeline.UseAuthentication();
    pipeline.UseAuthorization();
});
app.MapGet("/", () => "ok");
app.Run();
```

## What it wires

| Side | Concerns |
|---|---|
| `AddApiDefaults` | settings files (`appsettings.Target.json`, a local run's `appsettings.Local.json`; [configuration](../Foundation/Configuration/configuration.md#settings-files)) · Serilog (`UseSerilogConventional`) · TimeProvider · OTel tracing + metrics + OTLP · health checks · proxy-aware hosting · OpenAPI · trace-aware ProblemDetails · validation exception handler · per-IP rate limit · output cache · Brotli/Gzip compression · CORS (when origins given) · FluentValidation scan (when assemblies given) |
| `UseApiDefaults` | forwarded headers · OWASP secure headers · compression · routing · CORS · optional identity seam · rate limiter · output cache · OpenAPI endpoint · `/health` · readiness endpoint (when `ReadinessEndpointPath` is set) |

Every concern has an off-flag on `ApiDefaultsOptions`; defaults are all-on.

`ReadinessEndpointPath` (default `null`) maps a readiness endpoint that reports only the checks tagged
`ReadinessTag` (default `ready`, as `AddDatabaseReadinessCheck` tags), and `/health` then skips them: a host whose
database is down stays alive while its rollout gate waits. Without it, `/health` reports every check.

## Service host

`ServiceHost.RunAsync` is a service's whole `Program.cs`: it answers a one-off verb and exits with the verb's code, or
boots and serves.

```csharp
using Identity.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Meta;

return await ServiceHost.RunAsync(args,
    builder => builder.Configure(),
    app => app.ConfigureAsync(),
    verbs => verbs.AddPreHostVerb("keys", verb => SigningKeyCommand.Run(verb.Arguments, verb.Output, verb.Error, TimeProvider.System)));

public partial class Program;
```

| First argument | Runs | Stdout |
|---|---|---|
| a pre-host verb (`AddPreHostVerb`) | before any builder: no settings, logging or services | the verb's alone |
| `migrate` (built in) | `MigrateCommand` on the built host's services | the `migrate-result` line last |
| `config` (built in) | `config check` on the built host's services ([config check](../Foundation/Configuration/ConfigCheck/config-check.md)) | the `config-result` line only |
| a host verb (`AddHostVerb`) | on the built host's services | what the verb writes |
| anything else, or none | the config checks, then `configureApp`, then serving until shutdown | the host's logs |

- A host verb runs after `Build()` and before `configureApp`: no listener, hosted service or startup work; the host is
  disposed when the verb returns.
- A host verb's console logs go to stderr for the rest of the process, so the verb's output stays the last stdout line.
- A verb matches the first argument exactly; a known verb with wrong arguments answers its own usage error and exits
  non-zero, never serving.
- A serving boot runs the `config check` checks first: a deployed boot (`DEPLOY_ENVIRONMENT` set) refuses on a finding
  and exits with the check's code; a local run logs the findings and serves.
- `configureApp` takes `Action<WebApplication>` (`app => app.Configure()`) or `Func<WebApplication, Task>`
  (`app => app.ConfigureAsync()`).
- `migrate` and `config` are reserved; adding them, a name starting with `-`, or a name twice throws.
- Tests and an E2E tier pass their own `output`, `error` and `CancellationToken`; the process console is then left alone.

## Deliberately NOT included

Auth (`AddJwtBearerAuthentication`, OAuth providers, OTP), mediator, and data — they need per-app
decisions (keys, assemblies, connection strings). Register auth before `Build()`, then place its
middleware through the `UseApiDefaults` callback so identity-aware policies see the principal.

## See also

- Root [README.md](../../../../../README.md) — per-area composition when you need more control
- [Package registry](../../../../architecture/package-registry.md)
