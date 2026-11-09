# WoW.Two.Sdk.Beta.Be.Testing.Containers

> Base for Testcontainers-backed fixtures. Per-engine packages (Postgres, Redis, RabbitMq, etc.) extend `ContainerFixtureBase<TContainer>`.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Testing.Containers
```

You typically don't depend on this directly — use the engine-specific packages instead:

- `WoW.Two.Sdk.Beta.Be.Testing.Containers.Postgres`
- `WoW.Two.Sdk.Beta.Be.Testing.Containers.Redis`
- `WoW.Two.Sdk.Beta.Be.Testing.Containers.RabbitMq`
- `WoW.Two.Sdk.Beta.Be.Testing.Containers.Kafka`
- `WoW.Two.Sdk.Beta.Be.Testing.Containers.MongoDb`
- `WoW.Two.Sdk.Beta.Be.Testing.Containers.Azurite`

## Subclassing

```csharp
public sealed class PostgresFixture : ContainerFixtureBase<PostgreSqlContainer>
{
    public PostgresFixture() : base(new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build()) { }
    public override string Name => "postgres";
    public string ConnectionString => Container.GetConnectionString();
}
```

## Notes

- A shell-less (distroless) image, such as `nats`, needs a log-based wait: `UntilPortIsAvailable` and
  `UntilCommandIsCompleted` exec a shell probe inside the container, so `StartAsync` hangs forever and blame-hang
  reports a test host crash. Use `Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready")`
  (`NatsEventBusTests`).
- No `Testcontainers.Nats` module sits in CPM; NATS uses the generic `ContainerBuilder`.
- MinIO ships no public images any more (Docker Hub and quay deny the pull since 2026-09): S3 tests run
  `adobe/s3mock` on port 9090, any credentials, path-style (`S3BlobRepositoryTests`).

## See also

- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
