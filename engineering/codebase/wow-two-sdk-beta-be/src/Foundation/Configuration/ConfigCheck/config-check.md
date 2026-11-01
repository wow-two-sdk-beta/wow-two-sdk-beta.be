# WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck

> `app config check`: the boot's own settings validation, run without serving, so a bad setting is caught before a
> container restarts. Hub contract: `wow-two-ws/conventions/development/backend/dotnet/shapes/service/platform/startup/config-check.md`.

## Usage

`ServiceHost` (`Meta/`) answers the verb for every service; a service adds only its roots and its stranding checks, in
the `Add{Domain}` that binds the settings:

```csharp
services.AddValidatedSettings<PasswordSettings>(configuration, "Identity:Password", rules => rules
    .ValidateSetting(nameof(PasswordSettings.MinLength), "range",
        settings => settings.MinLength <= settings.MaxLength,
        settings => $"{settings.MinLength} exceeds MaxLength {settings.MaxLength}"));

services
    .AddSettingsRoot("Identity")                          // every key under Identity must bind to a record
    .AddStrandingCheck<SignInMethodStrandingCheck>();     // IStrandingCheck: indexed, read-only counts
```

```text
$ app config check
config-result {"verb":"check","exit":1,"service":"identity","invalid":[{"code":"range","setting":"Identity:Password:MinLength","message":"20 exceeds MaxLength 16"}],"unknown":["Identity:SignUp:Enabled"],"stranded":[],"durationMs":41}
```

A host without `ServiceHost` calls `ConfigCheckCommand.RunAsync(app.Services, "identity", args, Console.Out, Console.Error)`
after `Build()` and before `Configure`.

## Findings

| Finding | Source | Code |
|---|---|---|
| invalid value | every record registered through `AddValidatedSettings`, `AddModuleOptions` or `AddValidatedOptions`, resolved as the boot resolves it; then the host's startup validator for any other `ValidateOnStart` record | a data annotation's attribute (`required`, `range`, `min-length`); a `ValidateSetting` rule's own code; `type` for a value that does not convert; `invalid` for a plain `Validate` message |
| unknown key | each leaf key under a declared root (`AddSettingsRoot`) or a section bound through `AddValidatedSettings`, followed through the record's members, dictionary entries and collection indexes | — |
| stranding | each `IStrandingCheck`, in one fresh scope; counts of zero are dropped; none registered leaves it empty | the check's own |

- One run collects every finding; the setting is named, never its value.
- A module section (`AddModuleOptions`) is walked only under a declared root; `Logging`, `AllowedHosts` and any section
  outside the roots and the bound sections are never walked.
- A `ValidateSetting` failure reads `[{code}] {setting}: {message}` in the boot's own exception too.

## Exits

- `0` consistent: `invalid`, `unknown` and `stranded` all empty.
- `1` refused: an invalid value, an unknown key or a stranding count.
- `2` failed: a stranding check threw (an unreachable database), the validation could not finish, or any arguments other
  than `config check`; `error` replaces the three lists. A credential in an exception message is redacted from stderr.
- The last stdout line is the `config-result` line; the human report goes to stderr. `ConfigCheckResult.FromOutput`
  parses it back.

## Boot

`ServiceHost` runs the same checks on every serving boot, after `Build()` and before `Configure`:

- a deployed boot (`DEPLOY_ENVIRONMENT` set) logs each finding and exits with the check's code, never serving;
  a stranding count logs `Critical` with the setting, the count and the way out;
- any other run logs the findings as warnings and serves.

## Stranding checks

- Read the database in one read-only transaction through the service's own connection string.
- Count rows behind an index the owner's migration set creates; never a scan per row, never a personal value.
- Return one `StrandedCount(code, setting, count)` per rule; finish in seconds.
- A check that reads settings already reported invalid is skipped with a note, so the run still exits `1`.

## See also

- Service host and verb dispatch: [Meta](../../../Meta/Meta.md#service-host).
- The settings recipe: `Foundation/Options/OptionsRegistrationExtensions.cs`, `SettingRuleExtensions.cs`.
- Deployed vs local run: [configuration](../configuration.md#local-run).
