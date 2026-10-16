using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

/// <summary>Serializes the migration tests sharing one resettable PostgreSQL container.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresMigrationCollection : ICollectionFixture<MigratorPostgresFixture>
{
    /// <summary>The xUnit collection name.</summary>
    public const string Name = "Postgres migrations";
}
