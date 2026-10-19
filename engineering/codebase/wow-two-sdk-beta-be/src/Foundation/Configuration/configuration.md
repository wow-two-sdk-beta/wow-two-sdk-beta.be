# WoW.Two.Sdk.Beta.Be.Foundation.Configuration

> Env-var-overlay settings loader — binds an appsettings section (named after the settings type), then overlays `[EnvironmentVariable]`-marked properties from environment variables. Empty env vars are treated as absent; required properties throw when still unset.

## Usage

### Mark the overlaid properties

```csharp
public sealed class DatabaseConnectionSettings
{
    [EnvironmentVariable("DB_CONNECTION", required: true)]
    public string ConnectionString { get; set; } = null!;
}
```

### Load directly

```csharp
var settings = ConfigurationMapper.Load<DatabaseConnectionSettings>(configuration);
// section name defaults to typeof(T).Name ("DatabaseConnectionSettings"); pass a name to override
```

### Register into DI (binds + overlays, then exposes IOptions<T>)

```csharp
builder.Services.AddEnvironmentOverlaidOptions<DatabaseConnectionSettings>(builder.Configuration);
// resolve via the settings type directly or via IOptions<DatabaseConnectionSettings>
```

## Behavior

- Section name = `typeof(T).Name` unless overridden.
- Env-var value wins over the appsettings-bound value when present and the property is writable.
- An empty or whitespace-only env var is treated as null and does not override.
- A property marked `required: true` with no value after binding and overlay throws `InvalidOperationException`.
- The environment-variable name is the consumer's choice — the SDK bakes in none.

## Explicit environment contract

`UseEnvironmentAliases` replaces the environment-variable sources with a listed contract: each supported variable
maps to one configuration key, so an unrelated variable in the deployment can never override a setting.

```csharp
builder.AddSettingsFiles();
builder.Configuration.UseEnvironmentAliases(new Dictionary<string, string>
{
    ["DB_CONNECTION"] = "DB_CONNECTION",
    ["GOOGLE_CLIENT_ID"] = "Auth:Google:ClientId",
});
```

- Only set, non-empty variables override; `urls`, `http_ports` and `https_ports` keep their hosting values.
- Sources added afterwards, such as test-host overrides, still win.

## Settings files

`AddSettingsFiles` inserts two optional files right after `appsettings.{Environment}.json`. `AddApiDefaults` calls it
first; call it yourself only when the host reads settings before that.

| Order | Source | Read when |
|---|---|---|
| 1 | `appsettings.json` | always |
| 2 | `appsettings.{Environment}.json` | always |
| 3 | `appsettings.Target.json` | present; a deployed target's release bundle mounts it at `/app/` |
| 4 | `appsettings.Local.json` | a local run whose launch profile sets `LOCAL_SETTINGS=true` |
| 5 | user secrets, environment variables | the host's defaults |
| 6 | later sources: the vault, test overrides | added after the builder's defaults |

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddSettingsFiles();                                  // idempotent; AddApiDefaults calls it too
```

- `appsettings.Local.json` is the developer machine's own: gitignored, outside every image build context; a test host
  never sets `LOCAL_SETTINGS`, so it never reads the file.
- Transition shim: a deployed host (`DEPLOY_ENVIRONMENT` set) without `appsettings.Target.json` reads a legacy
  `appsettings.Local.json` in its place, mounted by a bundle older than pipelines v0.7.0, and logs a warning at start.
  A later release removes the shim. Contract: [`SettingsFiles.standard.md`](SettingsFiles.standard.md).

## Local run

Deployed dev runs as `Development`, so `IsDevelopment()` alone never means the developer's machine. `IsLocalRun` is
`Development` without `DEPLOY_ENVIRONMENT`, read from configuration and the process environment.

```csharp
if (builder.IsLocalRun())                                   // or app.Environment.IsLocalRun(app.Configuration)
    builder.Services.AddSingleton<ICodeSender, LogCodeSender>();
```

- `LOCAL_SETTINGS` plays no part; it only adds `appsettings.Local.json` on a local run.
- The SDK keys its own local-only defaults on it: EF Core sensitive-data logging and detailed errors, the OpenAPI
  endpoint and Data Protection without a key directory. Contract: [`LocalRun.standard.md`](LocalRun.standard.md).
