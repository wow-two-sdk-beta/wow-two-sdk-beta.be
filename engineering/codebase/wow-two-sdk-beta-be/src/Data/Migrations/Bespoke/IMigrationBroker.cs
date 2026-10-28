using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Defines reading raw migration SQL and seed files from a backing store.</summary>
/// <remarks>Use a filesystem source for the CLI and dev; use an embedded-resource source for self-contained runtime deploys.</remarks>
public interface IMigrationBroker
{
    /// <summary>Reads every numbered migration (folders matching <c>NNN-name</c>) with its Apply and Rollback SQL.</summary>
    /// <returns>The migrations, or an error when the source is incomplete or unreadable.</returns>
    Result<IReadOnlyList<RawMigration>> Read();

    /// <summary>Reads every seed file (<c>NNN-name.json</c>) beside the migrations; a source without seeds reads none.</summary>
    /// <returns>The seed files, or an error when the source is unreadable.</returns>
    Result<IReadOnlyList<RawSeed>> ReadSeeds() => Result<IReadOnlyList<RawSeed>>.Ok([]);
}
