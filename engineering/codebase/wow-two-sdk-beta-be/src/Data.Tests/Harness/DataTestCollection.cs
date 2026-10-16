using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Testing.Data.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Data.Tests.Harness;

/// <summary>The collection sharing one <see cref="DataTestDb"/> container across every test class in this suite.</summary>
[CollectionDefinition(Name)]
public sealed class DataTestCollection : ICollectionFixture<DataTestDb>
{
    /// <summary>Holds the collection name test classes attach to.</summary>
    public const string Name = "data";
}
