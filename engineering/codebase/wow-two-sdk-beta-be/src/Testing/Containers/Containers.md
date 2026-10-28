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

## See also

- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
