using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using NpgsqlTypes;

namespace WoW.Two.Sdk.Beta.Be.Caching.Postgres;

/// <summary>Stores <see cref="IDistributedCache"/> entries in a PostgreSQL table, so they survive restarts and deploys and every host sharing the database sees them.</summary>
/// <remarks>
///   - follows <c>Microsoft.Extensions.Caching.SqlServer</c>: absolute, relative and sliding expiration, the sliding window capped by the absolute one;
///     an entry set without any expiration slides by <see cref="PostgresDistributedCacheOptions.DefaultSlidingExpiration"/>
///   - a read never returns an expired row; a read or <see cref="Refresh"/> of a sliding entry moves its expiry, in the same statement
///   - a set upserts, so concurrent writers on any host never fail on the key; the last commit wins
///   - the clock is the registered <see cref="TimeProvider"/>, so tests drive expiry; every value travels as a parameter
///   - the table comes from the <c>distributed_cache</c> migration set; the cache never creates it
/// </remarks>
public sealed class PostgresDistributedCache : IDistributedCache
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _defaultSlidingExpiration;
    private readonly string _getSql;
    private readonly string _refreshSql;
    private readonly string _setSql;
    private readonly string _removeSql;
    private readonly string _sweepSql;

    /// <summary>Creates the cache over the configured table.</summary>
    /// <param name="options">The schema, table and default sliding expiration.</param>
    /// <param name="dataSource">The data source of the cache's database.</param>
    /// <param name="clock">The clock every expiry is computed against.</param>
    public PostgresDistributedCache(PostgresDistributedCacheOptions options, NpgsqlDataSource dataSource, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(clock);
        _dataSource = dataSource;
        _clock = clock;
        _defaultSlidingExpiration = options.DefaultSlidingExpiration;

        // Schema and table are validated lowercase identifiers; quoting them keeps any other text out of the statement.
        var table = $"\"{options.Schema}\".\"{options.TableName}\"";
        var slide = $"""
            UPDATE {table}
            SET expires_at = CASE
                WHEN absolute_expiration IS NOT NULL AND absolute_expiration < @now + sliding_expiration THEN absolute_expiration
                ELSE @now + sliding_expiration
            END
            WHERE id = @id
              AND expires_at >= @now
              AND sliding_expiration IS NOT NULL
              AND (absolute_expiration IS NULL OR absolute_expiration <> expires_at)
            """;
        _refreshSql = slide;
        _getSql = $"""
            WITH slid AS (
                {slide}
                RETURNING value
            )
            SELECT value FROM slid
            UNION ALL
            SELECT value FROM {table} WHERE id = @id AND expires_at >= @now AND NOT EXISTS (SELECT 1 FROM slid)
            """;
        _setSql = $"""
            INSERT INTO {table} (id, value, expires_at, sliding_expiration, absolute_expiration)
            VALUES (@id, @value, @expires_at, @sliding_expiration, @absolute_expiration)
            ON CONFLICT (id) DO UPDATE SET
                value = EXCLUDED.value,
                expires_at = EXCLUDED.expires_at,
                sliding_expiration = EXCLUDED.sliding_expiration,
                absolute_expiration = EXCLUDED.absolute_expiration
            """;
        _removeSql = $"DELETE FROM {table} WHERE id = @id";

        // SKIP LOCKED lets every host sweep at once; FOR UPDATE rechecks expiry on the latest row, so a concurrent refresh survives.
        _sweepSql = $"""
            DELETE FROM {table}
            WHERE id IN (
                SELECT id FROM {table}
                WHERE expires_at < @now
                LIMIT @batch
                FOR UPDATE SKIP LOCKED
            )
            """;
    }

    /// <inheritdoc />
    public byte[]? Get(string key)
    {
        using var command = Command(_getSql, key);
        return command.ExecuteScalar() as byte[];
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var command = Command(_getSql, key);
        await using (command.ConfigureAwait(false))
        {
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as byte[];
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The absolute expiration is not in the future.</exception>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        using var command = SetCommand(key, value, options);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The absolute expiration is not in the future.</exception>
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var command = SetCommand(key, value, options);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Refresh(string key)
    {
        using var command = Command(_refreshSql, key);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        var command = Command(_refreshSql, key);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        using var command = Command(_removeSql, key, withNow: false);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        var command = Command(_removeSql, key, withNow: false);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    /// <summary>Deletes every row past its expiry, in batches of <see cref="PostgresDistributedCacheConstants.SweepBatchSize"/>.</summary>
    /// <remarks>Rows another host is sweeping or refreshing are skipped, not waited for; the next sweep finds them if they are still expired.</remarks>
    /// <param name="cancellationToken">Cancels the sweep between or during batches.</param>
    /// <returns>The number of rows deleted.</returns>
    public async Task<int> DeleteExpiredItemsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var total = 0;
        int deleted;
        do
        {
            var command = _dataSource.CreateCommand(_sweepSql);
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", NpgsqlDbType.TimestampTz) { TypedValue = now });
                command.Parameters.Add(new NpgsqlParameter<int>("batch", NpgsqlDbType.Integer) { TypedValue = PostgresDistributedCacheConstants.SweepBatchSize });
                deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            total += deleted;
        }
        while (deleted == PostgresDistributedCacheConstants.SweepBatchSize);

        return total;
    }

    private NpgsqlCommand SetCommand(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var now = _clock.GetUtcNow();
        var absolute = AbsoluteExpirationOf(options, now);
        var sliding = options.SlidingExpiration;
        if (absolute is null && sliding is null)
            sliding = _defaultSlidingExpiration;

        var expiresAt = sliding is { } window
            ? (absolute is { } cap && cap < now + window ? cap : now + window)
            : absolute!.Value;

        var command = Command(_setSql, key, withNow: false);
        command.Parameters.Add(new NpgsqlParameter<byte[]>("value", NpgsqlDbType.Bytea) { TypedValue = value });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("expires_at", NpgsqlDbType.TimestampTz) { TypedValue = expiresAt });
        command.Parameters.Add(new NpgsqlParameter("sliding_expiration", NpgsqlDbType.Interval) { Value = sliding is { } s ? (object)s : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("absolute_expiration", NpgsqlDbType.TimestampTz) { Value = absolute is { } a ? (object)a : DBNull.Value });
        return command;
    }

    /// <summary>Resolves the absolute expiration as SQL Server's cache does: a relative one wins, a fixed one must lie ahead.</summary>
    /// <param name="options">The entry's options.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="InvalidOperationException">The fixed absolute expiration is not in the future.</exception>
    private static DateTimeOffset? AbsoluteExpirationOf(DistributedCacheEntryOptions options, DateTimeOffset now)
    {
        if (options.AbsoluteExpirationRelativeToNow is { } relative)
            return now + relative;

        if (options.AbsoluteExpiration is not { } absolute)
            return null;

        if (absolute <= now)
            throw new InvalidOperationException("The absolute expiration value must be in the future.");

        return absolute.ToUniversalTime();
    }

    /// <summary>Creates a command keyed by <paramref name="key"/>, with the current instant as <c>@now</c> unless told otherwise.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="withNow">Whether the statement reads <c>@now</c>.</param>
    /// <exception cref="ArgumentException">The key is empty or longer than <see cref="PostgresDistributedCacheConstants.MaxKeyBytes"/>.</exception>
    private NpgsqlCommand Command(string sql, string key, bool withNow = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Encoding.UTF8.GetByteCount(key) > PostgresDistributedCacheConstants.MaxKeyBytes)
            throw new ArgumentException($"A cache key may hold at most {PostgresDistributedCacheConstants.MaxKeyBytes} UTF-8 bytes.", nameof(key));

        var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter<string>("id", NpgsqlDbType.Text) { TypedValue = key });
        if (withNow)
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", NpgsqlDbType.TimestampTz) { TypedValue = _clock.GetUtcNow() });

        return command;
    }
}
