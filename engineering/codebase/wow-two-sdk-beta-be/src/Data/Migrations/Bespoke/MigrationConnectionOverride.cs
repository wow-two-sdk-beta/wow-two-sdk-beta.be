using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Holds the migrate key: a connection string that, once a migrate run turns it on, connects every set instead of the host's own connections.</summary>
/// <remarks>
///   - every set registration registers it dormant; the <c>migrate</c> command turns <see cref="MigrationRunMode"/> on, so the key connects every set for its run
///   - <c>AddMigrationConnectionOverride</c> turns it on for every run of the host instead; a boot never uses it
///   - inactive while the string is blank, so every set keeps its keyed or host connection
///   - never prints the string: it carries a credential
/// </remarks>
public sealed class MigrationConnectionOverride : IAsyncDisposable, IDisposable
{
    private readonly Lock _gate = new();
    private NpgsqlDataSource? _postgres;

    /// <summary>Creates the override over a connection string.</summary>
    /// <param name="connectionString">The migrate key's connection string, or null or blank for none.</param>
    public MigrationConnectionOverride(string? connectionString) =>
        ConnectionString = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();

    /// <summary>Gets the migrate key's connection string, or null when none is set.</summary>
    public string? ConnectionString { get; }

    /// <summary>Gets whether the override connects the sets.</summary>
    public bool IsActive => ConnectionString is not null;

    /// <summary>Gets a connection factory over the migrate key for a set's provider; every PostgreSQL set shares one data source.</summary>
    /// <param name="provider">The set's provider.</param>
    /// <exception cref="InvalidOperationException">The override is inactive.</exception>
    public IDbConnectionFactory GetFactory(DatabaseProvider provider)
    {
        var connectionString = ConnectionString ?? throw new InvalidOperationException("No migrate connection string is set.");
        if (provider == DatabaseProvider.Sqlite)
            return new SqliteConnections(connectionString);

        lock (_gate)
            return new DataSourceConnectionFactory(_postgres ??= NpgsqlDataSource.Create(connectionString));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Every set registration registers the override, so a provider disposed synchronously must dispose it too.</remarks>
    public void Dispose() => _postgres?.Dispose();

    /// <summary>Opens SQLite connections over the migrate key.</summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    private sealed class SqliteConnections(string connectionString) : IDbConnectionFactory
    {
        /// <inheritdoc />
        public DbConnection Create() => new SqliteConnection(connectionString);

        /// <inheritdoc />
        public async ValueTask<DbConnection> CreateOpenAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
    }
}
