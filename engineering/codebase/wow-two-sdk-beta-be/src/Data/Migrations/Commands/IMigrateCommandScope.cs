namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Defines the services one verb runs over and the host's part of <c>apply</c>: creating each set's database.</summary>
internal interface IMigrateCommandScope : IAsyncDisposable
{
    /// <summary>Gets the provider holding the registered sets, the engine and the strategy sets.</summary>
    IServiceProvider Services { get; }

    /// <summary>Gets the stamp recorded on rows <c>apply</c> and <c>seed</c> write, such as <c>deploy</c> or <c>cli</c>.</summary>
    string AppliedBy { get; }

    /// <summary>Creates each bespoke set's database when it is missing, before <c>apply</c> runs; the engine creates none.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task EnsureDatabasesAsync(CancellationToken ct);
}
