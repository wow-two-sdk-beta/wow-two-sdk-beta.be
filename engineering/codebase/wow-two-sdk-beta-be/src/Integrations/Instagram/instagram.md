# Instagram

One-page Instagram Business Discovery client in the core SDK. It uses Meta's
Facebook Login route and a querying professional account. Consumers provide
the token source, API version, querying account and publisher username.

Register `AddInstagramIntegration` with an explicit `IAccessTokenService`
factory and `BaseAddress = new Uri("https://graph.facebook.com/")`.
Configure non-secret transport bounds under `Integrations:Instagram`.
Credentials belong in the host's secret provider, resolved on every call.

The client returns selected media JSON plus cursor and sanitized request
diagnostics. It neither ingests nor stores posts. Consumers own publisher
allowlists, instance activation, page budgets, delayed retries and persistence.

- [API and usage](Instagram.spec.md)
- [Transport guarantees](Instagram.standard.md)
- [Meta Business Discovery](https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-user/business_discovery)
