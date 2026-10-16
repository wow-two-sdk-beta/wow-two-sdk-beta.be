# Web.SystemStatus

> The anonymous `GET api/system/status` endpoint: liveness plus the running build's version, commit and build time.

```csharp
app.MapSystemStatus(configure: o =>
{
    o.ServiceName = "Console";
    o.VersionAssembly = typeof(Program).Assembly;                              // default: the entry assembly
    o.Facts = context => [new("localRig", context.RequestServices.GetRequiredService<RigSettings>().IsLocal)];
});
// GET api/system/status → { "service": "Console", "status": "ok", "version": "1.4.2+3f9c2ab",
//                           "commit": "3f9c2ab", "builtAt": "2026-10-15T08:30:00+00:00", "localRig": false }
```

- `version` is the informational version (`-p:Version` plus the build-appended `+commit`); `unknown` when nothing declares one.
- `builtAt` comes from `[assembly: AssemblyMetadata("BuildTime", "<ISO-8601>")]`; a project stamps it with
  `<AssemblyMetadata Include="BuildTime" Value="$([System.DateTime]::UtcNow.ToString('o'))" />`.
- Configuration `Build:Version`, `Build:Commit`, `Build:Time` (env `Build__Version`, …) override the assembly values.
- A controller reads the same values with `BuildVersion.Read(typeof(Program).Assembly, configuration)`.
- Health probing stays on `/health`; this endpoint reports identity, never readiness.
