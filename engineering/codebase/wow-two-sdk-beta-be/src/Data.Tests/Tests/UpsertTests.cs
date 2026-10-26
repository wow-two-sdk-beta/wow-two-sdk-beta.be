using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness.Extensions;
using WoW.Two.Sdk.Beta.Be.Data.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Data.Tests.Tests;

/// <summary>Upserts insert, update and survive losing the insert race to a concurrent writer, on SQLite and PostgreSQL.</summary>
[Collection(DataTestCollection.Name)]
public sealed class UpsertTests(DataTestDb testDb)
    : RelationalTestBase<DataTestDb, DataTestDbContext>(testDb)
{
    private const string KeyConstraint = "ix_upsert_items_key";
    private readonly string _sqliteFile = Path.Combine(Path.GetTempPath(), $"upsert-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Upsert_InsertsAMissingRowThenUpdatesIt()
    {
        await using var context = await OpenSqliteAsync();

        var inserted = await context.UpsertAsync(item => item.Key == "a", Item("a", "one"), stored => stored.Value = "one");
        var updated = await context.UpsertAsync(item => item.Key == "a", Item("a", "two"), stored => stored.Value = "two");

        Assert.Equal(UpsertAction.Inserted, inserted.Action);
        Assert.Equal(UpsertAction.Updated, updated.Action);
        Assert.Same(inserted.Entity, updated.Entity);
        Assert.Equal(["a:two"], await RowsAsync(context));
    }

    [Fact]
    public async Task LostInsertRace_UpdatesTheWinnersRowInsteadOfFailing()
    {
        await using var rival = await OpenSqliteAsync();
        await using var context = await OpenSqliteAsync(new RaceInterceptor(() => InsertAsync(rival, "a", "winner")));

        var outcome = await context.UpsertAsync(item => item.Key == "a", Item("a", "mine"), stored => stored.Value = "mine");

        Assert.Equal(UpsertAction.Updated, outcome.Action);
        Assert.Equal(["a:mine"], await RowsAsync(context));
    }

    [Fact]
    public async Task LostInsertRace_KeepsOtherPendingChanges()
    {
        await using var rival = await OpenSqliteAsync();
        await using var context = await OpenSqliteAsync(new RaceInterceptor(() => InsertAsync(rival, "a", "winner")));
        context.Add(Item("b", "pending"));

        await context.UpsertAsync(item => item.Key == "a", Item("a", "mine"), stored => stored.Value = "mine");

        Assert.Equal(["a:mine", "b:pending"], await RowsAsync(context));
    }

    [Fact]
    public async Task AddIfAbsent_ReturnsTheWinnersRowUnchanged()
    {
        await using var rival = await OpenSqliteAsync();
        await using var context = await OpenSqliteAsync(new RaceInterceptor(() => InsertAsync(rival, "a", "winner")));

        var outcome = await context.AddIfAbsentAsync(item => item.Key == "a", Item("a", "mine"));
        var again = await context.AddIfAbsentAsync(item => item.Key == "a", Item("a", "later"));

        Assert.Equal(UpsertAction.Unchanged, outcome.Action);
        Assert.Equal("winner", outcome.Entity.Value);
        Assert.Equal(UpsertAction.Unchanged, again.Action);
        Assert.Equal(["a:winner"], await RowsAsync(context));
    }

    [Fact]
    public async Task PostgresConflictOnAnotherConstraint_StillThrows()
    {
        await using var context = await OpenPostgresAsync();
        var first = Item("a", "one");
        context.Add(first);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Same primary key, new unique key: the conflict is not the key's race.
        var duplicateId = new UpsertItem { Id = first.Id, Key = "b", Value = "two" };
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.UpsertAsync(item => item.Key == "b", duplicateId, stored => stored.Value = "two", KeyConstraint));
    }

    [Fact]
    public async Task PostgresLostRaceInsideATransaction_KeepsTheTransactionUsable()
    {
        await using var rival = await OpenPostgresAsync();
        await using var context = await OpenPostgresAsync(new RaceInterceptor(() => InsertAsync(rival, "a", "winner")));
        await using var transaction = await context.Database.BeginTransactionAsync();

        var outcome = await context.UpsertAsync(item => item.Key == "a", Item("a", "mine"), stored => stored.Value = "mine", KeyConstraint);
        context.Add(Item("b", "after"));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Equal(UpsertAction.Updated, outcome.Action);
        Assert.Equal(["a:mine", "b:after"], await RowsAsync(rival));
    }

    [Fact]
    public async Task PostgresConcurrentUpserts_AllSucceedOnOneRow()
    {
        await using (await OpenPostgresAsync())
        {
        }

        UpsertOutcome<UpsertItem>[] outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async writer =>
        {
            await using var context = Postgres();
            return await context.UpsertAsync(
                item => item.Key == "shared", Item("shared", $"w{writer}"), stored => stored.Value = $"w{writer}", KeyConstraint);
        }));

        await using var reader = Postgres();
        Assert.Single(await reader.Items.ToListAsync());
        Assert.Single(outcomes, outcome => outcome.Action == UpsertAction.Inserted);
    }

    public override Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_sqliteFile);
        return Task.CompletedTask;
    }

    private static UpsertItem Item(string key, string value) => new() { Id = Guid.NewGuid(), Key = key, Value = value };

    private static async Task InsertAsync(UpsertItemContext rival, string key, string value)
    {
        rival.Add(Item(key, value));
        await rival.SaveChangesAsync();
        rival.ChangeTracker.Clear();
    }

    private static async Task<string[]> RowsAsync(UpsertItemContext context)
    {
        context.ChangeTracker.Clear();
        return [.. (await context.Items.OrderBy(item => item.Key).ToListAsync()).Select(item => $"{item.Key}:{item.Value}")];
    }

    private async Task<UpsertItemContext> OpenSqliteAsync(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<UpsertItemContext>().UseSqlite($"Data Source={_sqliteFile}");
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        var context = new UpsertItemContext(builder.Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private UpsertItemContext Postgres(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<UpsertItemContext>().UseNpgsql(TestDb.ConnectionString);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new UpsertItemContext(builder.Options);
    }

    private async Task<UpsertItemContext> OpenPostgresAsync(IInterceptor? interceptor = null)
    {
        var context = Postgres(interceptor);
        if (interceptor is null)
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS upsert_items");
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
        }

        return context;
    }

    /// <summary>Runs a rival write just before the context's first save, so the upsert's read misses and its insert collides.</summary>
    private sealed class RaceInterceptor(Func<Task> rival) : SaveChangesInterceptor
    {
        private int _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await rival();
            }

            return result;
        }
    }

    /// <summary>A row with a unique business key beside its primary key.</summary>
    private sealed class UpsertItem
    {
        /// <summary>Gets or sets the primary key.</summary>
        public required Guid Id { get; set; }

        /// <summary>Gets or sets the unique business key.</summary>
        public required string Key { get; set; }

        /// <summary>Gets or sets the payload an upsert changes.</summary>
        public required string Value { get; set; }
    }

    /// <summary>The context over <see cref="UpsertItem"/>.</summary>
    /// <param name="options">The context options.</param>
    private sealed class UpsertItemContext(DbContextOptions<UpsertItemContext> options) : DbContext(options)
    {
        /// <summary>Gets the rows.</summary>
        public DbSet<UpsertItem> Items => Set<UpsertItem>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<UpsertItem>(entity =>
            {
                entity.ToTable("upsert_items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.Key).HasColumnName("key");
                entity.Property(item => item.Value).HasColumnName("value");
                entity.HasIndex(item => item.Key).IsUnique().HasDatabaseName("ix_upsert_items_key");
            });
    }
}
