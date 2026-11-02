using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Caching.Postgres;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

namespace WoW.Two.Sdk.Beta.Be.Data.Tests.Tests;

/// <summary>The PostgreSQL distributed cache keeps SQL Server's cache semantics on real PostgreSQL 18, under a migration set its host applies.</summary>
/// <remarks>Each test owns a fresh schema in the shared container and drops it afterwards; time moves only through the fake clock.</remarks>
[Collection(DataTestCollection.Name)]
public sealed class PostgresDistributedCacheTests(DataTestDb testDb) : IAsyncLifetime
{
    private const string SetName = PostgresDistributedCacheConstants.MigrationSetName;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly string[] FirstMigration = ["001-distributed-cache"];
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> _schemas = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var connection = await testDb.OpenConnectionAsync();
        foreach (var schema in _schemas)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SetAndGet_RoundTripTheValue_AndASecondSetReplacesIt()
    {
        await using var provider = await MigratedAsync(NewSchema());
        var cache = provider.GetRequiredService<IDistributedCache>();

        await cache.SetAsync("session:a", [1, 2, 3], new DistributedCacheEntryOptions());
        Assert.Equal(new byte[] { 1, 2, 3 }, await cache.GetAsync("session:a"));

        cache.Set("session:a", [4], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        Assert.Equal(new byte[] { 4 }, cache.Get("session:a"));
        Assert.Null(await cache.GetAsync("session:missing"));
        Assert.Throws<ArgumentException>(() => cache.Get(new string('k', PostgresDistributedCacheConstants.MaxKeyBytes + 1)));
    }

    [Fact]
    public async Task AbsoluteExpiration_HidesTheEntry_OnceItPasses()
    {
        await using var provider = await MigratedAsync(NewSchema());
        var cache = provider.GetRequiredService<IDistributedCache>();
        var now = _clock.GetUtcNow();

        await cache.SetAsync("relative", [1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        await cache.SetAsync("fixed", [2], new DistributedCacheEntryOptions { AbsoluteExpiration = now.ToOffset(TimeSpan.FromHours(5)).AddMinutes(5) });
        Assert.Throws<InvalidOperationException>(() =>
            cache.Set("past", [3], new DistributedCacheEntryOptions { AbsoluteExpiration = now.AddSeconds(-1) }));

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await cache.GetAsync("fixed"));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await cache.GetAsync("fixed"));
        Assert.NotNull(await cache.GetAsync("relative"));

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await cache.GetAsync("relative"));   // the expiry instant itself still reads
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await cache.GetAsync("relative"));
    }

    [Fact]
    public async Task SlidingExpiration_ExtendsOnEachRead_UntilTheAbsoluteCap()
    {
        await using var provider = await MigratedAsync(NewSchema());
        var cache = provider.GetRequiredService<IDistributedCache>();
        var sliding = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) };
        await cache.SetAsync("slides", [1], sliding);
        await cache.SetAsync("idle", [2], sliding);
        await cache.SetAsync("capped", [3], new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(25),
        });

        foreach (var _ in Enumerable.Range(0, 3))
        {
            _clock.Advance(TimeSpan.FromMinutes(8));   // 8, 16, 24: each read lands inside the window the last one opened
            Assert.NotNull(await cache.GetAsync("slides"));
            Assert.NotNull(await cache.GetAsync("capped"));
        }

        Assert.Null(await cache.GetAsync("idle"));
        _clock.Advance(TimeSpan.FromMinutes(2));   // 26: past the 25-minute cap, though the last read opened a window to 34
        Assert.Null(await cache.GetAsync("capped"));
        Assert.NotNull(await cache.GetAsync("slides"));
    }

    [Fact]
    public async Task Refresh_ExtendsTheSlidingWindow_WithoutReading()
    {
        await using var provider = await MigratedAsync(NewSchema());
        var cache = provider.GetRequiredService<IDistributedCache>();
        var sliding = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) };
        await cache.SetAsync("sync", [1], sliding);
        await cache.SetAsync("async", [2], sliding);

        _clock.Advance(TimeSpan.FromMinutes(8));
        cache.Refresh("sync");
        await cache.RefreshAsync("async");
        _clock.Advance(TimeSpan.FromMinutes(8));   // 16: alive only because the refresh at 8 moved expiry to 18

        Assert.NotNull(await cache.GetAsync("sync"));
        Assert.NotNull(await cache.GetAsync("async"));
    }

    [Fact]
    public async Task DefaultSlidingExpiration_Applies_WhenTheEntryNamesNone()
    {
        await using var provider = await MigratedAsync(NewSchema(), options => options.DefaultSlidingExpiration = TimeSpan.FromMinutes(3));
        var cache = provider.GetRequiredService<IDistributedCache>();
        await cache.SetAsync("read", [1], new DistributedCacheEntryOptions());
        await cache.SetAsync("unread", [2], new DistributedCacheEntryOptions());

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.NotNull(await cache.GetAsync("read"));
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.NotNull(await cache.GetAsync("read"));
        Assert.Null(await cache.GetAsync("unread"));
    }

    [Fact]
    public async Task Remove_DeletesTheEntry()
    {
        await using var provider = await MigratedAsync(NewSchema());
        var cache = provider.GetRequiredService<IDistributedCache>();
        await cache.SetAsync("sync", [1], new DistributedCacheEntryOptions());
        await cache.SetAsync("async", [2], new DistributedCacheEntryOptions());

        cache.Remove("sync");
        await cache.RemoveAsync("async");

        Assert.Null(cache.Get("sync"));
        Assert.Null(await cache.GetAsync("async"));
        Assert.Equal(0, await CountRowsAsync(_schemas[^1], PostgresDistributedCacheConstants.DefaultTableName));
    }

    [Fact]
    public async Task DeleteExpiredItems_DeletesOnlyExpiredRows()
    {
        string schema = NewSchema();
        await using var provider = await MigratedAsync(schema);
        var cache = provider.GetRequiredService<PostgresDistributedCache>();
        foreach (var index in Enumerable.Range(0, 3))
            await cache.SetAsync($"short:{index}", [1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        await cache.SetAsync("long", [2], new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) });

        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(3, await cache.DeleteExpiredItemsAsync());
        Assert.Equal(1, await CountRowsAsync(schema, PostgresDistributedCacheConstants.DefaultTableName));
        Assert.Equal(0, await cache.DeleteExpiredItemsAsync());
    }

    [Fact]
    public async Task Sweep_RunsOnTheInterval_AndDeletesExpiredRows()
    {
        string schema = NewSchema();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddPostgresDistributedCache(options =>
        {
            options.ConnectionString = testDb.ConnectionString;
            options.Schema = schema;
            options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(30);
        });
        using IHost host = builder.Build();
        (await host.Services.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var cache = host.Services.GetRequiredService<IDistributedCache>();
        await cache.SetAsync("short", [1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        await cache.SetAsync("long", [2], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });
        await host.StartAsync();

        // The first pass ran at start, before anything expired; each advance lets the job's next pass fire.
        var swept = false;
        for (var attempt = 0; attempt < 5 && !swept; attempt++)
        {
            _clock.Advance(TimeSpan.FromMinutes(30));
            swept = await EventuallyAsync(async () => await CountRowsAsync(schema, PostgresDistributedCacheConstants.DefaultTableName) == 1, TimeSpan.FromSeconds(2));
        }

        Assert.True(swept, "The sweep did not delete the expired row.");
        Assert.NotNull(await cache.GetAsync("long"));
        await host.StopAsync();
    }

    [Fact]
    public async Task MigrationSet_CreatesTheTable_RerunsAsNoOp_AndPassesLintAndIdempotency()
    {
        string schema = NewSchema();
        await using var provider = Build(schema);

        var first = (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var second = (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();

        Assert.Equal(FirstMigration, first.Single(set => set.Set == SetName).Applied);
        Assert.Empty(second.Single(set => set.Set == SetName).Applied);
        Assert.True(await TableExistsAsync(schema, PostgresDistributedCacheConstants.DefaultTableName));
        Assert.Equal(1, await CountRowsAsync(schema, $"{SetName}_migration_history"));
        Assert.Equal("module", provider.GetRequiredService<IMigrationSetRegistry>().GetOptions(SetName).SchemaOwner);

        await MigrationLint.AssertCleanAsync(provider, lint => lint.ExpandOnlyAfter(SetName, 0));
        var broker = provider.GetRequiredService<IMigrationSetRegistry>().GetBroker(SetName);
        string proving = NewSchema();
        await using var migrator = MigratorHarness.CreatePostgres(testDb.ConnectionString, sets => sets.Add(SetName, _ => broker, options =>
        {
            options.SchemaName = proving;
            options.SchemaOwner = PostgresDistributedCacheConstants.SchemaOwner;
        }));
        await migrator.AssertIdempotentAsync(SetName);
    }

    [Fact]
    public async Task HostSection_RenamesSchemaAndTable_ForTheSetAndTheCache()
    {
        string schema = NewSchema();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Postgres:ConnectionString"] = testDb.ConnectionString,
            ["Caching:Postgres:Schema"] = schema,
            ["Caching:Postgres:TableName"] = "sessions",
        });
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddPostgresDistributedCache(options => options.Schema = "overridden_by_the_section");
        using IHost host = builder.Build();

        (await host.Services.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var cache = host.Services.GetRequiredService<IDistributedCache>();
        await cache.SetAsync("session:a", [7], new DistributedCacheEntryOptions());

        Assert.Equal(schema, host.Services.GetRequiredService<IMigrationSetRegistry>().GetOptions(SetName).SchemaName);
        Assert.True(await TableExistsAsync(schema, "sessions"));
        Assert.False(await TableExistsAsync(schema, PostgresDistributedCacheConstants.DefaultTableName));
        Assert.Equal(new byte[] { 7 }, await cache.GetAsync("session:a"));
    }

    [Fact]
    public async Task TwoHosts_MigrateAndWriteOneKeyConcurrently_WithoutFailing()
    {
        string schema = NewSchema();
        await using var first = Build(schema);
        await using var second = Build(schema);

        var applied = await Task.WhenAll(first.ApplyMigrationSetsAsync("first"), second.ApplyMigrationSetsAsync("second"));
        Assert.All(applied, result => result.ValueOrThrow());
        Assert.Equal(1, applied.Sum(result => result.ValueOrThrow().Single(set => set.Set == SetName).Applied.Count));

        var caches = new[] { first.GetRequiredService<IDistributedCache>(), second.GetRequiredService<IDistributedCache>() };
        await Task.WhenAll(Enumerable.Range(0, 40).Select(index =>
            caches[index % 2].SetAsync("shared", [(byte)index], new DistributedCacheEntryOptions())));

        byte[]? seenByFirst = await caches[0].GetAsync("shared");
        Assert.NotNull(seenByFirst);
        Assert.Equal(seenByFirst, await caches[1].GetAsync("shared"));
        Assert.Equal(1, await CountRowsAsync(schema, PostgresDistributedCacheConstants.DefaultTableName));
    }

    [Fact]
    public void Registration_ReplacesTheMemoryCache_RegisteredBeforeOrAfter()
    {
        var before = new ServiceCollection();
        before.AddDistributedMemoryCache();
        before.AddPostgresDistributedCache(options => options.ConnectionString = testDb.ConnectionString);

        var after = new ServiceCollection();
        after.AddPostgresDistributedCache(options => options.ConnectionString = testDb.ConnectionString);
        after.AddPostgresDistributedCache(options => options.DefaultSlidingExpiration = TimeSpan.FromMinutes(7));
        after.AddDistributedMemoryCache();

        foreach (var services in new[] { before, after })
        {
            using var provider = services.BuildServiceProvider();
            Assert.IsType<PostgresDistributedCache>(provider.GetRequiredService<IDistributedCache>());
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        }

        using var twice = after.BuildServiceProvider();
        var options = twice.GetRequiredService<PostgresDistributedCacheOptions>();
        Assert.Equal(TimeSpan.FromMinutes(7), options.DefaultSlidingExpiration);
        Assert.Equal(testDb.ConnectionString, options.ConnectionString);
    }

    [Fact]
    public async Task MissingConnectionString_FailsTheHostStart_YetTheSetStillDescribes()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddPostgresDistributedCache();
        using IHost host = builder.Build();

        var registry = host.Services.GetRequiredService<IMigrationSetRegistry>();
        Assert.Equal(PostgresDistributedCacheConstants.DefaultSchema, registry.GetOptions(SetName).SchemaName);
        Assert.True(registry.GetBroker(SetName).Read().IsSuccess);

        var error = await Record.ExceptionAsync(() => host.StartAsync());
        Assert.NotNull(error);
        Assert.Contains("ConnectionString is required", error.ToString(), StringComparison.Ordinal);
    }

    private string NewSchema()
    {
        string schema = "cache_test_" + Guid.NewGuid().ToString("N")[..12];
        _schemas.Add(schema);
        return schema;
    }

    private ServiceProvider Build(string schema, Action<PostgresDistributedCacheOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddPostgresDistributedCache(options =>
        {
            options.ConnectionString = testDb.ConnectionString;
            options.Schema = schema;
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider();
    }

    private async Task<ServiceProvider> MigratedAsync(string schema, Action<PostgresDistributedCacheOptions>? configure = null)
    {
        var provider = Build(schema, configure);
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        return provider;
    }

    private async Task<long> CountRowsAsync(string schema, string table)
    {
        await using var connection = await testDb.OpenConnectionAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT count(*) FROM \"{schema}\".\"{table}\"";
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private async Task<bool> TableExistsAsync(string schema, string table)
    {
        await using var connection = await testDb.OpenConnectionAsync();
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT to_regclass(@name) IS NOT NULL";
        exists.Parameters.Add(new NpgsqlParameter("name", $"{schema}.{table}"));
        return (bool)(await exists.ExecuteScalarAsync())!;
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + (within < Patience ? within : Patience);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(25);
        }

        return await condition();
    }
}
