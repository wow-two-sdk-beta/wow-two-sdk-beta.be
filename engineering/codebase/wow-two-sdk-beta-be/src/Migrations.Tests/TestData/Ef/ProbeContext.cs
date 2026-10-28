using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.TestData.Ef;

/// <summary>An EF context whose one migration the strategy-set tests apply through the migrate tree.</summary>
/// <param name="options">The context options.</param>
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    /// <summary>The probe rows.</summary>
    public DbSet<ProbeItem> Items => Set<ProbeItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ProbeItem>().ToTable("ef_probe_items").Property(item => item.Id).HasColumnName("id");
}

/// <summary>A probe row.</summary>
public sealed class ProbeItem
{
    /// <summary>The key.</summary>
    public int Id { get; set; }
}

/// <summary>Creates the probe table.</summary>
[DbContext(typeof(ProbeContext))]
[Migration("20260101000000_Items")]
public sealed class ItemsMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("create table ef_probe_items(id integer primary key);");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("drop table ef_probe_items;");
}
