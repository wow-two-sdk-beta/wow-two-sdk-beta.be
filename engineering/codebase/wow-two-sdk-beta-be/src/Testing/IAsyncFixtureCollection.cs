namespace WoW.Two.Sdk.Beta.Be.Testing;

/// <summary>
/// Defines behavior that aggregates multiple <see cref="IAsyncTestFixture"/>s and orchestrates start/stop/reset across them.
/// </summary>
public interface IAsyncFixtureCollection : IAsyncTestFixture
{
    /// <summary>The fixtures composed into this collection.</summary>
    IReadOnlyCollection<IAsyncTestFixture> Fixtures { get; }
}
