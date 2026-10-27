# WoW.Two.Sdk.Beta.Be.Web.SecureHeaders

> OWASP-flavored secure-headers middleware. Wraps `NetEscapades.AspNetCore.SecurityHeaders` with API-shaped defaults.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Web.SecureHeaders
```

## Usage

```csharp
var app = builder.Build();
app.UseOwaspSecureHeaders();
```

Headers applied: HSTS · X-Content-Type-Options · X-Frame-Options: DENY · Referrer-Policy · Permissions-Policy · Cross-Origin-{Opener,Embedder,Resource}-Policy · Server header removed.

## Cross-origin isolation

`SecureHeadersOptions` drops the opener or embedder policy for a host that a cross-origin popup or an un-CORP'd subresource would otherwise break. Both default on.

```csharp
app.UseOwaspSecureHeaders(headers => headers.EnableCrossOriginEmbedderPolicy = false);
```

A host whose pages open OAuth popups, such as Google Identity Services, keeps the opener policy but relaxes it to
`same-origin-allow-popups`:

```csharp
app.UseOwaspSecureHeaders(headers => headers.AllowOpenerPopups = true);
builder.AddApiDefaults(o => o.AllowCrossOriginOpenerPopups = true);   // the boot-floor equivalent
```

Under the `AddApiDefaults` boot floor the same two flags sit on `ApiDefaultsOptions` and are forwarded:

```csharp
builder.AddApiDefaults(o => o.EnableCrossOriginEmbedderPolicy = false);
```

## Cross-origin embedding

Every response carries `Cross-Origin-Resource-Policy: same-origin`. An endpoint that another origin loads in place —
media a native app's web view plays, a poster image, a download — opts in to `cross-origin`; the rest keep the preset.

```csharp
[AllowCrossOriginEmbedding]                               // a controller or an action
public IActionResult Content(Guid id) => PhysicalFile(...);

app.MapGet("/media/{id}", ...).AllowCrossOriginEmbedding();  // a minimal endpoint or group
```

Static files answer before routing, so they carry no endpoint to mark. A folder of them that other origins embed — a
file-icon set, say — is opted in by path prefix, segment by segment (`/icons` covers `/icons/pdf.svg`, not `/iconset`):

```csharp
app.UseOwaspSecureHeaders(headers => headers.CrossOriginEmbeddingPaths.Add("/icons"));
builder.AddApiDefaults(o => o.CrossOriginEmbeddingPaths.Add("/icons"));   // the boot-floor equivalent
```

## Endpoint overrides

An endpoint whose content needs its own value for a preset header answers it after the preset; a null value removes
the header. The Files content host, for one, sandboxes user content and drops the embedder policy:

```csharp
group.OverrideSecureHeader("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; sandbox")
     .OverrideSecureHeader("Cross-Origin-Embedder-Policy", null);

[SecureHeaderOverride("X-Frame-Options", "SAMEORIGIN")]   // a controller or an action
```

## See also

- [NetEscapades.AspNetCore.SecurityHeaders](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders)
- [OWASP secure headers](https://owasp.org/www-project-secure-headers/)
