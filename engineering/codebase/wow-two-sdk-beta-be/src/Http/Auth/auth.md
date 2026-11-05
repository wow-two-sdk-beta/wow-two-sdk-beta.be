# WoW.Two.Sdk.Beta.Be.Http.Auth

> Outbound credentials for one named `HttpClient`: service tokens (RFC 6749), person tokens exchanged
> for one audience (RFC 8693, with RFC 8707 resource indicators) and client certificates (mutual TLS).

Namespaces:

- `…Http.Auth.OAuth2ClientCredentials` — a service calling as itself
- `…Http.Auth.OAuth2TokenExchange` — a service calling on a person's behalf
- `…Http.Auth.OAuth2` — shared: `OAuth2TokenClientOptions`, `OAuth2TokenRequestException`, `OAuth2HttpClientNames`
- `…Http.Auth.Mtls` — client certificates

## Client credentials

```csharp
services.AddHttpClient("billing", client => client.BaseAddress = new Uri("https://billing.internal/"))
    .AddOAuth2ClientCredentials(options =>
    {
        options.TokenEndpoint = new Uri("https://identity.internal/connect/token");
        options.ClientId = "orders";
        options.ClientSecret = secret;
        options.Resource = "https://billing.internal";   // optional, sent as `resource`
        options.Scope = "invoices.read";                 // optional
    });
```

## Token exchange

```csharp
services.AddHttpClient("billing", client => client.BaseAddress = new Uri("https://billing.internal/"))
    .AddOAuth2TokenExchange(options =>
    {
        options.TokenEndpoint = new Uri("https://identity.internal/connect/token");
        options.ClientId = "orders";
        options.ClientSecret = secret;
        options.Resource = "https://billing.internal";   // required
    });
```

- The default `IOAuth2SubjectTokenSource` reads the current request's `Authorization: Bearer` token.
- Its session key is the authenticated user's `sid` and `org_id` claims, else the SHA-256 hex of the token.
- A call with no person throws `InvalidOperationException`; jobs with no person use `AddOAuth2ClientCredentials`.
- An own source replaces the default with `AddSingleton<IOAuth2SubjectTokenSource, …>`; it must stay singleton-safe,
  because handlers run in their own DI scope.
- One session key shares one exchanged token until it expires, so another claim that changes inside a session stays
  stale until then; a source that needs the change puts it in `SessionKey`.

## Cache keys

One singleton cache serves both grants; the client name is not in the key.

| Grant | Key |
|---|---|
| Client credentials | grant + endpoint + client id + resource + scope + additional parameters |
| Token exchange | grant + endpoint + client id + session key + resource + scope + additional parameters |

- Two named clients for one audience share a token; two audiences never do.
- Additional parameters enter the key sorted by name, so their insertion order changes nothing.
- A subject token and a client secret never enter a key.
- A token refreshes at `expires_in` minus `RefreshSkew`; the skew is capped at half the lifetime.
- One key fetches once at a time; a fixed set of 64 gates serves all keys, so two cold keys may wait on one gate.
- Each fetch sweeps expired entries, so the cache holds only tokens still live.

## Failures

| Case | Result |
|---|---|
| Resource answers `401` | evicts that key's token; the request is not retried |
| Request already carries `Authorization` | passes through; nothing is fetched or evicted |
| Token endpoint answers non-success | `OAuth2TokenRequestException`: `Error`, `ErrorDescription`, `StatusCode` |
| Success body has no `access_token` | `AppException` (`ExternalUnavailable`) |
| Missing `TokenEndpoint`, `ClientId` or exchange `Resource` | `InvalidOperationException` |

- The exception's message holds the client name, the status and a registered error code, never server text.

## Testing

Swap the token endpoint's primary handler by its public name:

```csharp
services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint)
    .ConfigurePrimaryHttpMessageHandler(() => fakeEndpoint);
```
