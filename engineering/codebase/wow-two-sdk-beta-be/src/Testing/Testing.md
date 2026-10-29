# WoW.Two.Sdk.Beta.Be.Testing

> Core test host (`WebApiTestHost<T>`), `WebApiTestBase<T>` xUnit base, and async fixture interfaces.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Testing
```

## Usage

### Inheriting `WebApiTestBase<TProgram>`

```csharp
using WoW.Two.Sdk.Beta.Be.Testing;

public class GreetingsTests : WebApiTestBase<Program>
{
    [Fact]
    public async Task Get_returns_hello()
    {
        var resp = await Client.GetStringAsync("/hello");
        Assert.Equal("Hello", resp);
    }

    [Fact]
    public async Task Time_advances_via_fake_clock()
    {
        Clock.Advance(TimeSpan.FromHours(1));
        // ... time-sensitive assertions
    }
}
```

### Composing with container fixtures

```csharp
public class PaymentTests : WebApiTestBase<Program>
{
    private readonly PostgresFixture _pg = new();

    public override async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override WebApiTestHost<Program> BuildHost() => new()
    {
        ConfigureServicesHook = services =>
        {
            services.AddDbContext<MyDb>(o => o.UseNpgsql(_pg.ConnectionString));
        }
    };
}
```

### Scripted outbound HTTP (`Http/`)

```csharp
var github = new ScriptedHttpMessageHandler();
github.Json["repos/o/r/commits/main"] = "...";                       // path + query, no leading slash
github.Raw["repos/o/r/contents/icon.svg?ref=abc"] = svgBytes;
github.Failures["repos/o/r/git/trees/abc?recursive=1"] = HttpStatusCode.Forbidden;
github.Answers["POST app/installations/7/access_tokens"] =                 // method + path, any status, built per call
    () => ScriptedHttpMessageHandler.JsonAnswer(HttpStatusCode.Created, """{"token":"ghs_1"}""");

services.AddGitHubIntegration().ConfigurePrimaryHttpMessageHandler(() => github);   // typed client
var factory = new SingleClientHttpClientFactory(github, new Uri("https://api.github.com/")); // IHttpClientFactory callers

github.Requests.Should().ContainSingle(r => r.Authorization == "Bearer token");
```

- `Answers` win over `Failures`, which win over `Json`, then `Raw`; unscripted paths answer `404`.
- `Requests` records method, host, path, `Route` (`GET repos/o/r`), headers, `Authorization` and body;
  `CountOf(pathAndQuery)` and `CountOfRoute(route)` count them.
- `SingleClientHttpClientFactory` hands every name a client over the one handler and never disposes it.

### Problem responses (`Web/HttpExtensions`)

- `response.ReadProblemAsync()` parses an RFC 7807 body (SDK `code` / `errors` land in `Extensions`); it throws with the raw body when the response is not a problem.

## Spec

- [Standard](Testing.standard.md)
- [Spec](Testing.spec.md)

## See also

- `Microsoft.AspNetCore.Mvc.Testing` (the underlying lib)
- `Microsoft.Extensions.TimeProvider.Testing` for `FakeTimeProvider`
- `Respawn` for DB reset (transitively included)
