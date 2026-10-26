# Package registry

*Last updated: 2026-10-26*

> The eight published NuGets and the capability areas compiled into them.

## Published package family

| Package | Role |
|---|---|
| `WoW2.Sdk.Beta.Be` | production mono library |
| `WoW2.Sdk.Beta.Be.Abstractions` | BCL-only kernel: `Result` family, `AppError` family, `IEvent`, `ICurrentUserService` / `UserKind`; core depends on it (unreleased, first ships with the next core release) |
| `WoW2.Sdk.Beta.Be.Data.Abstractions` | web-free data contracts |
| `WoW2.Sdk.Beta.Be.Data.Migrations.Cli` | `wow-migrate` .NET tool |
| `WoW2.Sdk.Beta.Be.Testing` | base test helpers |
| `WoW2.Sdk.Beta.Be.Testing.Data` | data test helpers |
| `WoW2.Sdk.Beta.Be.Testing.Integrations` | integration-client test doubles |
| `WoW2.Sdk.Beta.Be.Testing.Messaging` | messaging test harness |

All eight share one evaluated version and publish in lockstep. The rows below track capability areas,
not additional package outputs.

The family shipped as `WoW2.Sdk.Backend.Beta[.*]` (namespace `WoW.Two.Sdk.Backend.Beta`) through
`10.0.66-beta`; the version sequence continues under the `WoW2.Sdk.Beta.Be` IDs.

---

## Capability status

### Published in 10.0.59-beta

The [September 26 sweep](../research/sdk-completion/sweep-2026-09-26.md) adds `Data.Sessions`
(`AddDataSession<TContext>`, nested units, borrowed connections, completion hooks),
`Mediator.DataUnits` (`ITransactionalRequest`, `AddMediatorDataUnitInterceptor`), and `Http.Safety`
(`AddSafeOutboundHttp`). Dapper reads gain soft-delete/xmin policies; idempotency gains atomic
ownership, release and commit-aware replay. Release `6924fa6` (tag `v10.0.59-beta`) also carries the
optional calendar end and test-host dependency validation found during ForeverPin adoption.

### Published in 10.0.58-beta

The [ForeverPin SDK adoption cut](../research/foreverpin-adoption/foreverpin-adoption.md) adds these mono-library capabilities;
publication was recorded in the adoption track. Consumer upgrades are separate.

- `Codes`: bounded SVG/PNG for every declared symbology, reusable validators and typed render results.
- `Codes.Payloads`: `AddCodePayloads`, typed scanner serializers, vCard and floating VEVENT exporters.
- `Foundation.Identifiers`: `IIdGenerator` / `CryptographicIdGenerator`.
- `Web.RequestContext`: `IAcceptLanguageParser` / `AcceptLanguageParser`, `UserAgentDeviceMapper`.
- `Identity.Guest`: authenticated, expiring guest capabilities shared with current-user resolution.
- `Web.ExceptionHandling`: `ControllerProblemExtensions.ToProblem` uses the registered factory.
- `Data.Errors` and `Foundation.Serialization`: wrapped database classification and closed immutable subtype registration.


> Status: **stub** = placeholder · **scaffold** = registration/minimal API · **shipped** = included in the published mono library · **planned** = absent. These older capability rows are not separate package outputs. Provider runtime verification must be read from the owning test evidence.

---

## Meta — composition root

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be` (root, `src/Meta/`) | `AddApiDefaults()` / `UseApiDefaults()` — one-import P1 boot floor (Serilog + OTel + health + proxy hosting + OpenAPI + problem details + rate limit + output cache + compression + optional CORS/validators). Auth/mediator/data stay explicit. | shipped |

## P0 — Testing scaffold (parallel track)

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Testing` | Core test host (`WebApiTestHost<TProgram>`), `WebApiTestBase<TProgram>` xUnit base, async fixture interfaces | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Assertions` | Bundles AwesomeAssertions + Shouldly | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Bogus` | `BogusFakerFactory` deterministic-seed factory | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Verify` | `VerifyDefaults.Initialize()` + Verify.{Xunit, AspNetCore, Http} | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.WireMock` | `WireMockFixture` (WireMock.Net async fixture) | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers` | `ContainerFixtureBase<TContainer>` for engine packages | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.Postgres` | `PostgresFixture` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.Redis` | `RedisFixture` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.RabbitMq` | `RabbitMqFixture` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.Kafka` | `KafkaFixture` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.MongoDb` | `MongoDbFixture` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.Azurite` | `AzuriteFixture` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Data` | Data-tier helpers (companion → core + EF): `MigratorHarness` (PG + SQLite), `MigratorPostgresFixture` (drop-schema reset), `MigratorTestBase`, `MigrationsWorkspace`, `MigrationHistoryRow`, `DisableBespokeMigrator()`, `RemoveAllForDbContext`/`RepointDbContext` | shipped |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.Elasticsearch` | Elasticsearch Testcontainers fixture | planned |
| `WoW.Two.Sdk.Beta.Be.Testing.Containers.Localstack` | LocalStack (AWS emulator) fixture | planned |

## P1 — Boot floor

### Foundation

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Time` | `AddTimeProviders`, `TimeZoneHelpers`, `CronExpressionParser`; NodaTime + Cronos + TimeZoneConverter | shipped |
| `WoW.Two.Sdk.Beta.Be.Foundation.Errors` | `AppError` / `AppErrorType` / `AppException` / `AppErrorFactory` value model (kernel `…Be.Abstractions`, namespace kept); `IExceptionMapper`, mapping rules and `IErrorNatureMapper` stay in core | shipped |
| `WoW.Two.Sdk.Beta.Be.Foundation.Results` | `Result`, `Result<T>`, `Result<TSuccess, TFailure>` + `Bind` / `Tap` / `Ensure` / `Combine` / `Attempt` (kernel `…Be.Abstractions`, namespace kept) | shipped |
| `WoW.Two.Sdk.Beta.Be.Validation` | `AddFluentValidatorsFromAssemblies` (FluentValidation assembly scan) | shipped |
| `WoW.Two.Sdk.Beta.Be.Serialization` | `JsonOptionsPresets` (camelCase + NodaTime + lenient input) | shipped |
| `WoW.Two.Sdk.Beta.Be.Guards` | `IdentifierGuardExtensions` (`NotSlug`, `NotUlid`) on top of Ardalis.GuardClauses | shipped |
| `WoW.Two.Sdk.Beta.Be.ValueObjects` | Re-exports Vogen + StronglyTypedId source generators | shipped |
| `WoW.Two.Sdk.Beta.Be.Naming` | **Casing authority** — `CaseStyle` enum (10 styles), `WordTokenizer` (acronym/digit-aware split), `CaseConverter` + string extensions, reversible `EnumNameConverter<TEnum>`. Zero deps. Source for column / enum-label / SQL-identifier casing. | shipped |

### Observability

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Observability` | Meta — wires logging + tracing + metrics + health | planned |
| `WoW.Two.Sdk.Beta.Be.Observability.Logging` | `StartupFailureReportingService` + `UseSerilogConventional` — durable pre-host failures, then Serilog → `ILogger<T>` | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.Tracing` | `AddOpenTelemetryTracing` — OTel tracer + AspNetCore/HttpClient/Grpc/SqlClient/EFCore/Redis instrumentation | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.Metrics` | `AddOpenTelemetryMetrics` — OTel meter + AspNetCore/HttpClient/Runtime/Process | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.HealthChecks` | `AddHealthChecksBuilder` — `IHealthChecksBuilder` + Xabaril provider deps (SqlServer/Postgres/MySql/Redis/RabbitMQ/Mongo/Kafka/Elastic/Network/Uris/AzureSB/AzureStorage/AwsS3/AwsSqs) | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.Otlp` | `AddOtlpExporters` — OTLP gRPC/HTTP for traces + metrics | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.Prometheus` | `AddPrometheusMetricsExporter` — scrape endpoint | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.AzureMonitor` | `AddAzureMonitorExporter` — Azure Monitor distro | shipped |
| `WoW.Two.Sdk.Beta.Be.Observability.Datadog` | `Datadog.Trace` re-export (use OTLP route by default) | shipped |

### Web

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Web` | Meta — wires hosting + openapi + problem details + secure-headers + cors + ratelimit + outputcache | planned |
| `WoW.Two.Sdk.Beta.Be.Web.Hosting` | `AddProxyAwareHosting` / `UseProxyAwareHosting` — forwarded headers + host filtering (`AllowedHosts`) + bounded request limits + request decompression | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.RequestLimits` | `AddRequestLimits` — Kestrel body/header/request-line/headers-timeout caps (bounds the decompression input) | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.OpenApi` | `AddOpenApiDefaults` / `MapOpenApiEndpoint` — `Microsoft.AspNetCore.OpenApi` (.NET 10) | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.OpenApi.Swashbuckle` | Swashbuckle fallback adapter | planned |
| `WoW.Two.Sdk.Beta.Be.Web.ProblemDetails` | `AddTraceAwareProblemDetails` plus opt-in `AddHttpProblemDetails` / `UseHttpProblemDetails` — RFC 9457 errors, trace context and MVC normalization | implemented (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Http.Errors` | `HttpExceptionMappingRule` (in `AddApiDefaults`) + `ToAppErrorAsync` / `EnsureSuccessAsync` — outbound failures as `AppError` | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging` | `PageDto<T>` / `TokenPageDto<T>` (UI SDK `Page` / `TokenPage` shapes), `PageApiRequest` / `TokenPageApiRequest`, EF `ToPageAsync` + composite-key `ToTokenPageAsync` with opaque tokens | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Web.Idempotency` | `Idempotency-Key` execute-once + replay (422 on body reuse, 409 in flight) over `IIdempotencyRepository`; activated by the `HttpIdempotency` section | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Web.ConditionalRequests` | ETag + `304` for GET, `SetEntityTag` / `FailsIfMatch` for versioned writes; activated by the `ConditionalRequests` section, inert in `UseApiDefaults` | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Web.ErrorTranslation` | `IErrorTranslationService` behind the default error mappers; activated by `Validation:Translation` (validation module options); `ToApiFailure<T>` envelope helper; `AddErrorTexts(culture, texts)` lets a package contribute the texts for its own error codes (`ErrorTextSpec`, unreleased) | implemented (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Web.Captcha` | `AddCaptcha()` (`Web:Captcha`, off by default) + `.RequireCaptcha(action)` — Turnstile, hCaptcha, reCAPTCHA v2/v3 siteverify; hostname, action and score rules; fail-closed outages | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Web.Quotas` | `AddQuotas()` (`Quotas`, off by default) + `.RequireQuota(name)` + `MapQuotaUsageEndpoint()` — per-plan allowances in UTC windows, refunds on failure, in-memory or Redis counters | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Web.RateLimit` | `AddPerIpSlidingWindowRateLimit` — sliding window per-IP, 100 req/min default; `AddConfiguredRateLimits` — named/global policies from the `RateLimits` section, problem-details 429 (unreleased) | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.OutputCache` | `AddDefaultOutputCache` — built-in middleware with default 60s policy | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.SecureHeaders` | `UseOwaspSecureHeaders` — OWASP-flavored preset (HSTS, CSP-friendly, COOP/COEP/CORP) | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.Cors` | `AddDefaultCorsPolicy` — single-line CORS policy registration | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.Compression` | `AddBrotliGzipCompression` — Brotli + Gzip with `Fastest` level | shipped |
| `WoW.Two.Sdk.Beta.Be.Web.Versioning` | `AddDefaultApiVersioning` — URL/header/query versioning with `1.0` default | shipped |

## P2 — Request pipeline + auth

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Mediator` | `IMediator` + `IRequest<T>` + `IPipelineBehavior<,>` — MediatR-API-compatible facade (no MediatR dep, MIT) | shipped |
| `WoW.Two.Sdk.Beta.Be.Mediator.Validation` | `AddMediatorValidationBehavior()` — FluentValidation pipeline behavior | shipped |
| `WoW.Two.Sdk.Beta.Be.Mediator.Logging` | `AddMediatorLoggingBehavior()` — source-gen `[LoggerMessage]` request/response logging | shipped |
| `WoW.Two.Sdk.Beta.Be.Mediator.Authorization` | `AddMediatorAuthorizationBehavior()` + `IRequireAuthorization` marker | shipped |
| `WoW.Two.Sdk.Beta.Be.Mediator.Idempotency` | `AddMediatorDeduplicatingInterceptor()` + `IIdempotent` + `IIdempotencyRepository`; ownership-token contract since `10.0.59-beta`; durable `AddSqlIdempotencyRepository()` (PostgreSQL/SQLite, leases) implemented, unreleased; durable `AddRedisIdempotencyRepository` (Lua-atomic, lease by expiry, 2026-09-28) | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity` | Meta — JWT + cookies + Identity API endpoints | planned |
| `WoW.Two.Sdk.Beta.Be.Identity.Core` | `AddUserAccounts<TUser>()` — own sliced-lego user model: 7 entities + `ApplyIdentitySchema` + `IUserRepository`/`EfUserRepository` + `UserAccountService` + `UserClaimsPrincipalFactory`; opt-in slices (implemented, unreleased): passwords + breach check, purpose tokens, email, lockout, security-stamp revocation, roles, user claims, external logins, two-factor, phone, sign-in, refresh tokens, account HTTP API (`MapUserAccountEndpoints`) ([track](../research/identity/identity-slices.md)) | shipped core; slices implemented |
| `WoW.Two.Sdk.Beta.Be.Identity.Jwt` | `AddJwtBearerAuthentication()` — JWT bearer (symmetric or JWKS) | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Cookies` | `AddCookieAuthentication()` — secure cookie defaults | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.ApiKeys` | `AddApiKeyAuthentication(keys, gate)` + `UseApiKeyAccessGate()` — marked secrets stored as SHA-256, the `ApiKey` scheme (marked Bearer or `X-Api-Key`), a local-or-key gate with open and local-only paths; product-owned `IApiKeyRepository` (TranscriptForge v0.7) | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Identity.Oidc` | `AddOpenIdConnectAuthentication()` — Authorization Code + PKCE | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Google` | `AddGoogleAuthentication(clientId, clientSecret)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Microsoft` | `AddMicrosoftAuthentication(clientId, clientSecret)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.GitHub` | `AddGitHubAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Apple` | `AddAppleAuthentication(clientId, teamId, keyId, keyPath)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Facebook` | `AddFacebookAuthentication(appId, appSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.LinkedIn` | `AddLinkedInAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Discord` | `AddDiscordAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Slack` | `AddSlackAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.GitLab` | `AddGitLabAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Amazon` | `AddAmazonAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Twitch` | `AddTwitchAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Spotify` | `AddSpotifyAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Yandex` | `AddYandexAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Reddit` | `AddRedditAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Notion` | `AddNotionAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.OAuth.Vkontakte` | `AddVkontakteAuthentication(clientId, clientSecret, scopes)` | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Mfa.Totp` | `TotpService` — secret gen + otpauth URI + verify with ±1 step tolerance | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Mfa.WebAuthn` | `AddFido2WebAuthn(domain, name, origins)` — Fido2.AspNet | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData` | `.AddPersonalData()` — `manage/personal-data` export (identity sections + `IPersonalDataExporter<TUser>`) and `manage/delete-account` erasure (`IAccountDeletionHandler<TUser>` then identity rows) | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.Core.EmailSignIn` | `.AddEmailSignIn()` (`Identity:EmailSignIn`) — single-use link or code sign-in over the OTP service, `email-sign-in` routes, `login/two-factor` ticket completion | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys` | `.AddPasskeys(o => …)` (`Identity:Passkeys`) — `identity_passkeys`, `UserPasskeyService`, usernameless `PasskeySignInAsync`, single-use ceremonies, account routes | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations` | `.AddOrganizations(o => …)` (`Identity:Organizations`) — `identity_organizations` + `identity_memberships`, `OrganizationService`, configured roles with permissions, `org_role` / `org_permission` claims, last-owner guard | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.PasswordHashing.Argon2` | `UseArgon2PasswordHasher<TUser>()` — OWASP 2024 baseline | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Jwt.Issuance` | `AddJwtTokenIssuance()` — `ITokenIssuer` HMAC issuance (HS256/384/512), per-call lifetime/audience overrides; pure lib, no ASP.NET dependency | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Otp` | `AddOtpService()` (`Identity:Otp`) — `IOtpService` keyed by (subject, scope) with per-call `OtpCodeSpec` (numeric · alphanumeric · letters); `IOtpMessageFormatter` per-culture ICU templates (built-in en/ru/uz); `IOtpDeliveryHandlerFactory` keyed channels | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram` | `AddTelegramOtpDelivery()` — chat-id delivery via consumer-registered `ITelegramBotClient` (Telegram.Bot, MIT) | shipped |
| `WoW.Two.Sdk.Beta.Be.Identity.Otp.WhatsApp` | `AddWhatsAppOtpDelivery()` — `whatsapp` channel: authentication template per culture, free-text fallback | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.Otp.TelegramGateway` | `AddTelegramGatewayOtpDelivery()` — `telegram-gateway` channel: codes to a phone number via the Telegram Gateway API | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Identity.Otp.Email` | `AddEmailOtpDelivery()` — `email` channel over `IEmailBroker` | implemented (mono-lib, unreleased) |

## P3 — Persistence + outbound

### Data

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Data` | Meta — wires Abstractions + EF Core + Audit + SoftDelete + NamingConventions + Json + Ef Migrations runner | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.Abstractions` | Entity contracts — `IEntity`, `IKeyedEntity<TId>`, `IHasTableName` (static-abstract `TableName`), `IAuditable`/`ICreationAuditable`/`IModificationAuditable` (+ `…By<TUserId>` actor variants), `ISoftDeletable`/`ISoftDeletableBy<TUserId>`, `IHasTenant<TTenantId>`, `IRowVersioned`, `IHasXmin`, `IVersioned`; repository contracts `IReadRepository<TEntity,TId>` + `IRepository<TEntity,TId>` (Create/Update/Delete/Get verbs). Zero deps. | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore` | `AppDbContextBase` + `AddEntityFrameworkCore<TContext>` (pooling + config scanner) | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.Specs` | provider-neutral `EntitySpec` (table, key, columns, indexes, concurrency kinds, soft delete, tenant) + `IEntitySpecConfiguration<T>` + `EntitySpecRegistry`; `AddEntitySpecs`; `Data:Specs:Unsupported` Throw/Skip | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Specs` | `ApplyEntitySpecs` EF mapper; `AppDbContextBase` applies registered specs and rotates stamp tokens | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Data.Dapper.Repositories` (specs) | `DapperRepository` reads a registered `EntitySpec`: renamed table/columns, ignored and store-written properties, all four token kinds | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.SqlServer` | `UseSqlServerConventional` — retry-on-failure (6×), 30s command timeout | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Postgres` | `UseNpgsqlConventional` — retry + NpgsqlDataSource overload for enum mapping; `AddNpgsqlDataSource`; `MapEnums(style, nsFilter, …, assemblies)` (reflection bulk enum registration via casing engine, kills double-registry) + `CaseStyleNameTranslator`; `ApplyNpgsqlConventions` (xmin token) | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.MySql` | `UseMySqlConventional` — Pomelo + AutoDetect server version | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite` | `UseSqliteConventional` — 30s command timeout | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Cosmos` | `UseCosmosConventional` — connection-string + endpoint+key overloads | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Audit` | `AuditInterceptor` (SaveChangesInterceptor) + `IAuditCurrentUserService` for `CreatedBy`/`UpdatedBy` | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.SoftDelete` | `SoftDeleteInterceptor` (DELETE→UPDATE) + `ApplySoftDeleteFilter` ModelBuilder ext | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.NamingConventions` | `UseSnakeCase/LowerCase/CamelCase/UpperSnakeCaseNamingConvention` | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Json` | `JsonValueConverter<T>` + `JsonValueComparer<T>` + `HasJsonConversion()` PropertyBuilder ext | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore` (Naming) | `EnumCaseConverter<TEnum>` (reversible string enum via `Naming`) + `HasEnumStringConversion()` PropertyBuilder ext | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore` (Repositories) | `EfRepository<TEntity,TId>` (thin CRUD) + `AddEfRepositories<TContext>` / `AddEfRepository<TRepo,TEntity,TId>` DI helpers | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Bulk` | EFCore.BulkExtensions re-export (BulkInsert/Update/Delete) | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Triggered` | `UseTriggersConventional` + `AddTriggersFromAssemblies` | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Projectables` | `UseProjectablesConventional` — `[Projectable]` computed properties | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness` | `TrySaveChangesUniqueAsync`, `SaveWithGeneratedKeyAsync`; `UpsertAsync` / `AddIfAbsentAsync` — keyed upserts that re-read the winner after a lost insert race (savepoint inside a transaction) | implemented (mono-lib, unreleased upserts) |
| `WoW.Two.Sdk.Beta.Be.Data.Dapper` | `AddDapperConventions` (snake_case + DateOnly + ListTypeHandler), `IDbConnectionFactory`, `SqlNaming` (`Col`/`Par`/`Table`, string + expression overloads), `EnumTypeHandler<TEnum>` (reversible string enum, `AddEnumTypeHandler<T>`); `DapperRepository<TEntity,TId>` (thin CRUD, SQL from `IHasTableName`+`SqlNaming`) + `AddDapperRepository` DI helpers | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.Migrations.Ef` | `AddEfMigrationsRunner<TContext>` — hosted service with connect-retry | scaffold |
| `WoW.Two.Sdk.Beta.Be.Data.Migrations.DbUp` | `AddDbUpRunner` — hosted service + provider factories (Postgres/SqlServer/MySql/Sqlite) | scaffold |

### Caching

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Caching` | Meta — HybridCache defaults | planned |
| `WoW.Two.Sdk.Beta.Be.Caching.Hybrid` | `AddHybridCaching`, `ICacheRepository`, cache key builder and HybridCache adapter | shipped (mono-lib; distributed invalidation remains) |
| `WoW.Two.Sdk.Beta.Be.Caching.Memory` | `AddInMemoryCaching` | shipped (mono-lib; distributed invalidation remains) |
| `WoW.Two.Sdk.Beta.Be.Caching.Redis` | `AddRedisDistributedCache` as the optional L2 backend | shipped (mono-lib; distributed invalidation remains) |
| `WoW.Two.Sdk.Beta.Be.Caching.SqlServer` | SQL Server as L2 backend | planned |
| `WoW.Two.Sdk.Beta.Be.Caching.Cosmos` | Cosmos as L2 backend | planned |
| `WoW.Two.Sdk.Beta.Be.Caching.FusionCache` | FusionCache as alt | planned |

### Http / outbound

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Http` | Meta — Refit + resilience defaults | planned |
| `WoW.Two.Sdk.Beta.Be.Http.Refit` | `AddRefitApiClient<TApi>(baseAddr, …)` — Refit client + SDK JSON + resilience baked in; `CreateDefaultRefitSettings()` | scaffold |
| `WoW.Two.Sdk.Beta.Be.Http.Resilience` | `AddSdkResilience()` (`IHttpClientBuilder` ext) + tunable `HttpResilienceOptions` over `Microsoft.Extensions.Http.Resilience` (Polly v8) | scaffold |
| `WoW.Two.Sdk.Beta.Be.Http.Core` | `AddResilientClient<TClient>` / `AddResilientClient(name, …)` — plain typed/named HttpClient + SDK resilience | scaffold |
| `WoW.Two.Sdk.Beta.Be.Http.Hedging` | `AddSdkHedging()` — standard-hedging preset (parallel attempt racing) + tunable `HttpHedgingOptions`; for idempotent latency-sensitive calls | scaffold |
| `WoW.Two.Sdk.Beta.Be.Http.HeaderPropagation` | `AddConventionalHeaderPropagation(…)` + `AddPropagatedHeaders()` — X-Correlation-Id / X-Request-Id defaults over `Microsoft.AspNetCore.HeaderPropagation` | scaffold |
| `WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials` | `AddOAuth2ClientCredentials()` — client_credentials bearer handler; per-client-name token cache, single-flight fetch, refresh skew | scaffold |
| `WoW.Two.Sdk.Beta.Be.Http.Auth.Mtls` | `AddMutualTls()` — client-certificate `SocketsHttpHandler` (loaded cert or PKCS#12 path) | scaffold |
| `WoW.Two.Sdk.Beta.Be.Integrations.Processes` | `AddProcessClient()` / keyed `AddProcessClient(name, …)` → `IProcessClient` (`RunAsync`, `RunJsonAsync`, `Start`) — argument list, environment allowlist, timeout that kills the tree, capped output, `JsonFailureLineParser`, failures marked by `ProcessFailureKind` | implemented (mono-lib, unreleased) |

## P4 — Distributed essentials

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Messaging` | Meta — DotNetCore.CAP outbox defaults | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap` | DotNetCore.CAP wrapping | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap.Postgres` | CAP storage on Postgres | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap.SqlServer` | CAP storage on SQL Server | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap.MongoDb` | CAP storage on MongoDB | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap.RabbitMq` | CAP transport via RabbitMQ | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap.Kafka` | CAP transport via Kafka | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Cap.AzureServiceBus` | CAP transport via ASB | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.RabbitMq` | `AddRabbitMqEventBus` — RabbitMQ transport behind the port (native DLX/DLQ) | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Messaging.Kafka` | `AddKafkaEventBus` — Kafka transport (emulated DLQ) | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Messaging.AzureServiceBus` | `AddAzureServiceBusEventBus` — topic + subscription-per-endpoint with `Subject` correlation filters, native DLQ, sessions, scheduled enqueue | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Messaging.AzureEventHubs` | Azure.Messaging.EventHubs direct use | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.AwsSqs` | AWSSDK.SQS direct use | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Nats` | `AddNatsEventBus` — NATS JetStream transport (emulated DLQ, in-context settle) | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Messaging.RedisStreams` | `AddRedisStreamsEventBus` — consumer group + `XPENDING`/`XCLAIM` stale recovery, emulated DLQ, in-context settle | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Messaging.Mqtt` | MQTTnet direct use | planned |
| `WoW.Two.Sdk.Beta.Be.Messaging.Webhooks` | `AddWebhooks` — outbound HMAC-signed delivery + SSRF guard + retry | shipped (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Persistence` | `AddWebhookEntityFrameworkStores<TContext>()` (`Webhooks:Persistence`) + `ApplyWebhookSchema()` — EF subscriptions (Data-Protection secrets), delivery log with bodies, `WebhookRedeliveryService`, purge | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound` | `AddInboundWebhooks()` (`Webhooks:Inbound`) + `.RequireWebhookSignature(name)` — wow2/Standard/Stripe/GitHub/Shopify/Slack/Paddle/Telegram validators, replay window, delivery-id dedupe | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Jobs` | Meta — Hangfire defaults | planned |
| `WoW.Two.Sdk.Beta.Be.Jobs.Hangfire` | `AddHangfireJobs(storage, opts)` (plus a provider-aware `(provider, config)` storage overload) + `AddInMemoryHangfireJobs()` (dev) + `UseHangfireJobsDashboard()` (local-only default). SDK serializer conventions, worker/queue/schedule-polling tuning. **Hangfire is LGPL-3.0 — sole exception to permissive-only, per targets.md P4** | shipped |
| `WoW.Two.Sdk.Beta.Be.Jobs.Hangfire.Postgres` | `AddPostgresHangfireJobs(connStr, opts, storage)` — PostgreSQL storage preset; `AddPostgresHangfireJobs(opts, storage)` / `UsePostgresPersistenceStorage(provider)` reuse the persistence floor's `DatabaseSettings` | shipped |
| `WoW.Two.Sdk.Beta.Be.Jobs.Hangfire.SqlServer` | Hangfire on SQL Server | planned |
| `WoW.Two.Sdk.Beta.Be.Jobs.Hangfire.Redis` | Hangfire on Redis | planned |
| `WoW.Two.Sdk.Beta.Be.Jobs.Batches` | `AddBatchPipeline<TItem, THandler>()` — bounded in-memory pipeline, scoped batch handler, shutdown drain, drop/processed/failed metrics | implemented |
| `WoW.Two.Sdk.Beta.Be.Jobs.Core` | `AddPeriodicJob<TService>(name, run, configure)` — interval loop (0 = off, read at start), fresh scope per pass, log and continue, pass timeout, fixed delay or rate, pass metrics | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Jobs.Coravel` | Coravel alt | planned |
| `WoW.Two.Sdk.Beta.Be.Jobs.NCronJob` | NCronJob alt | planned |
| `WoW.Two.Sdk.Beta.Be.Comms` | Meta — email + SMS + push abstractions | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Email` | `IEmailBroker` + `EmailMessage`/`EmailSendResult` (result-typed) + `AddEmailDefaults()` (From/Reply-To defaults) | shipped |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.Templates` | `AddEmailTemplates()` (`Comms:Email:Templates`) — Markdown templates per culture, ICU placeholders, branded inlined-style layout, text twin, buttons, `ITemplatedEmailService.SendAsync` | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.MailKit` | `AddMailKitEmailBroker()` — SMTP via MailKit (any relay; mailpit/mailhog dev) | shipped |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.SendGrid` | `AddSendGridEmailBroker()` — SendGrid v3 API | shipped |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.Mailgun` | Mailgun impl | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.Postmark` | Postmark impl | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.Ses` | `AddSesEmailBroker()` — SES v2 simple send (no attachments; raw-MIME future) | shipped |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.Acs` | Azure Communication Services email impl | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Email.FluentEmail` | FluentEmail templating | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Sms` | `ISmsBroker` + `SmsMessage` + `AddSmsDefaults`; brokers keyed by name, `ISmsBrokerFactory`, `AddSmsBrokers(configuration)`; OTP delivery via `AddSmsOtpDelivery` (`Identity/Otp/Sms`) | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Sms.Twilio` | `AddTwilioSmsBroker` — Messages API over HttpClient | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Sms.Vonage` | `AddVonageSmsBroker` — SMS API over HttpClient | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Sms.Eskiz` | `AddEskizSmsBroker` — Uzbekistan gateway, cached bearer token | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Sms.Plivo` | Plivo impl | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Sms.Acs` | Azure Comm Services SMS impl | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.WhatsApp` | `IWhatsAppBroker` + `WhatsAppTemplate` + `IWhatsAppBrokerFactory`; `AddWhatsAppBrokers(configuration)` registers each configured provider | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.WhatsApp.Meta` | `AddMetaWhatsAppBroker` — WhatsApp Cloud API over HttpClient | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.WhatsApp.Twilio` | `AddTwilioWhatsAppBroker` — Twilio WhatsApp sender, content-SID templates | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Push` | `IPushBroker` + `PushMessage` + `PushSendResult.TokenInvalid` | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Push.Fcm` | `AddFcmPushBroker` — HTTP v1, service-account RS256 assertion, cached OAuth token | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Push.Apns` | `AddApnsPushBroker` — HTTP/2, `.p8` ES256 provider token (no dotAPNS) | implemented (mono-lib, unreleased) |
| `WoW.Two.Sdk.Beta.Be.Comms.Push.OneSignal` | OneSignal impl | planned |
| `WoW.Two.Sdk.Beta.Be.Comms.Push.WebPush` | `AddWebPushBroker` — VAPID (RFC 8292) + `aes128gcm` payload encryption (RFC 8291), no third-party library | implemented (mono-lib, unreleased) |

## P5 — SaaS-shaped

| Package | Niche | Status |
|---|---|---|
| `WoW.Two.Sdk.Beta.Be.Tenancy` | Meta — Finbuckle defaults | planned |
| `WoW.Two.Sdk.Beta.Be.Tenancy.Finbuckle` | Finbuckle.MultiTenant wrapping | planned |
| `WoW.Two.Sdk.Beta.Be.Tenancy.PerRow` | EF Core query-filter strategy | planned |
| `WoW.Two.Sdk.Beta.Be.Tenancy.PerDb` | Per-tenant DbContext factory | planned |
| `WoW.Two.Sdk.Beta.Be.Tenancy.PerSchema` | Per-tenant schema strategy | planned |
| `WoW.Two.Sdk.Beta.Be.Ai` | `Microsoft.Extensions.AI` 10.x chat pipeline and provider abstractions | implemented (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Ai.SemanticKernel` | SK wrapping | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.KernelMemory` | KernelMemory wrapping | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.OpenAi` | OpenAI provider | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.AzureOpenAi` | Azure OpenAI provider | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Anthropic` | Anthropic Claude provider | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Bedrock` | AWS Bedrock provider | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Gemini` | Google Gemini provider | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Ollama` | `AddOllamaChatClient` / `AddOllamaEmbeddingGenerator` — OllamaSharp 5.5.0 | implemented (mono-lib) |
| `WoW.Two.Sdk.Beta.Be.Ai.Llama` | LLamaSharp local inference | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.Pgvector` | pgvector + Npgsql vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.Redis` | Redis Stack vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.Qdrant` | Qdrant vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.Pinecone` | Pinecone vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.Milvus` | Milvus vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.Chroma` | Chroma vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Vector.AzureSearch` | Azure AI Search vector store | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Tokenizers` | Microsoft.ML.Tokenizers preset | planned |
| `WoW.Two.Sdk.Beta.Be.Ai.Mcp` | `AddStatelessMcpServer(guard)` / `MapAuthenticatedMcp` — stateless HTTP transport with host and operation authorization; request guard (foreign `Origin` 403, 64 KiB body 413) and `McpToolErrorFilter` (`AppError` → retryable `McpToolError`) on by default | implemented (mono-lib; guard and tool errors unreleased) |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags` | `AddFeatureFlags()` + `IFeatureFlags`; gates: `RequireFeatures(...)` endpoint filter and `AddMediatorFeatureGatingInterceptor()` (`IFeatureGated`, gates implemented, unreleased) | shipped |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags.OpenFeature` | `AddOpenFeatureClient()` — OpenFeature seam | shipped |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags.LaunchDarkly` | LaunchDarkly provider | planned |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags.ConfigCat` | ConfigCat provider | planned |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags.Unleash` | Unleash provider | planned |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags.GrowthBook` | GrowthBook provider | planned |
| `WoW.Two.Sdk.Beta.Be.FeatureFlags.Esquio` | Esquio provider | planned |

## P6 — Heavy domain extensions

(Listed compactly; details TBD when phase starts.)

| Group | Packages |
|---|---|
| Realtime | `…Realtime.SignalR`, `…Realtime.SignalR.Redis`, `…Realtime.SignalR.AzureSignalR`, `…Realtime.Sse`, `…Realtime.WebSockets`, `…Realtime.MagicOnion` |
| Storage | **`…Storage` core + `…Storage.Local` — shipped** (`IBlobRepository`, `AddLocalBlobStorage`) · **`…Storage.S3` — implemented, unreleased** (`AddS3BlobStorage`; AWS, R2 and MinIO endpoints; S3Mock round-trip test) · **`…Storage.Azure` — implemented, unreleased** (`AddAzureBlobStorage`; Azurite round-trip test) · **`…Storage` signed URLs — implemented, unreleased** (`IBlobUrlIssuer`: S3 SigV4 presign, Azure SAS incl. user delegation, HMAC transfer routes `MapBlobTransferEndpoints`) · **atomic file writes — implemented, unreleased** (`AtomicFileExtensions`; `LocalFileBlobRepository` saves atomically) · planned: `…Storage.Gcs`, `…Storage.FluentStorage` |
| Media | **`…Media.Word` — implemented, unreleased** (`IWordService` over the Open XML SDK: info, text/Markdown extraction, split-run template fill with row repeats, Markdown → DOCX; `Media:Word`; `MapWordToolEndpoints`) · **`…Media.Markdown` — implemented, unreleased** (`IMarkdownService` over Markdig: safe HTML, plain text, headings, front matter, reading time) · **`…Media.Excel` import — implemented, unreleased** (`IExcelParser` + `ITabularParser` for CSV and XLSX; row-precise `TabularRowException`) · **`…Media.Endpoints` — implemented, unreleased** (`MapImageToolEndpoints`, `MapPdfToolEndpoints`: multipart tool APIs) · **`…Media.Pdf` — implemented, unreleased** (`IPdfService` over PDFsharp + PdfPig: info, merge, split, extract/reorder, remove, rotate, images → PDF, compression, forms, metadata, AES-256, text, watermark, page numbers; `Media:Pdf`) · **`…Media.Images` — implemented, unreleased** (`IImageService` over SkiaSharp: probe, edit — orient/crop/resize/compress/text/watermark — collage, BlurHash/colors/perceptual hash; `Media:Images`) · **`…Media.Captions` — shipped** (`VttCaptionParser` + `CaptionSegment`/`CaptionTrack` + `AddVttCaptionParser()`, `src/Media/Captions/`, extracted from the TranscriptForge POC; `ITimedText` slicing, `TimedTextRenderer` and `CaptionClockMapper` from TranscriptForge v0.5) · **`…Media.YouTube` — shipped** (`YouTubeUrlMapper`, `YouTubeLinkExtractor`, `src/Media/YouTube/`, TranscriptForge v0.5) · planned: `…Media.ImageSharp`, `…Media.Skia`, `…Media.Magick`, `…Media.FFmpeg`, `…Media.QuestPdf`, `…Media.ClosedXml`, `…Media.CsvHelper`, `…Media.Markdig` |
| Search | `…Search.PostgresFts`, `…Search.Elasticsearch`, `…Search.OpenSearch`, `…Search.Meilisearch`, `…Search.Algolia`, `…Search.Typesense`, `…Search.Lucene` |
| Workflow | `…Workflow.Stateless`, `…Workflow.Elsa`, `…Workflow.WorkflowCore`, `…Workflow.DurableTask`, `…Workflow.Temporal` |
| Aspire | `…Aspire.AppHost`, `…Aspire.ServiceDefaults`, `…Aspire.Integrations.Redis`, … (40+ Aspire integrations) |
| OIDC server | `…Identity.Server.OpenIddict`, `…Identity.Server.Duende` (commercial-aware companion) |
| GraphQL / OData | `…GraphQL.HotChocolate`, `…GraphQL.GraphQLDotNet`, `…OData` |
| Payment | `…Payments`, `…Payments.Stripe`, `…Payments.Paddle`, `…Payments.Adyen`, `…Payments.Braintree`, `…Payments.Square` |
| Geo | **`…Geo.IpLocation` — implemented** (`IIpLocationBroker` over MMDB via `MaxMind.Db`, `AddDbIpLiteIpLocation` monthly free refresh, `src/Geo/IpLocation/`) · planned: `…Geo.Nts`, `…Geo.Geocoding`, `…Geo.H3` |
| Documents | `…Documents.OCR.Tesseract`, `…Documents.OCR.AzureDocumentIntelligence`, `…Documents.PdfSharp`, `…Documents.iText` |
| Tooling | `…Tools.Cli`, `…Tools.Build.Nuke`, `…Tools.SourceGen.Common` |

---

> **Maintenance**: this file is updated whenever a package is added, status changes, or a package is renamed. The single source of truth — folder structure under `src/` should match this table 1:1.
