# Data protection

> A Data Protection key ring that survives restarts, so cookies and tokens outlive a container replacement.

```csharp
builder.Services.AddPersistentDataProtection(options =>
{
    options.ApplicationName = "ForeverPin";
    options.KeyDirectory = builder.Configuration["DataProtection:KeyPath"];
});
```

- Outside a local run, deployed dev included, startup fails without `KeyDirectory`: ephemeral keys void every cookie
  on restart. A local run is `Development` without `DEPLOY_ENVIRONMENT` ([configuration](../../Foundation/Configuration/configuration.md#local-run)).
- Hosts that read each other's cookies or tokens share `ApplicationName` and the key directory.
- Authentication cookies, antiforgery tokens and guest capabilities all use this key ring.
- Keys are stored unencrypted; mount the directory on a volume only the app can read.
