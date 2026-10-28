using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.SoftDelete;

/// <summary>Model-builder extensions that install a global query filter on every <see cref="ISoftDeletable"/> entity type, excluding rows whose <see cref="ISoftDeletable.DeletedAt"/> is set.</summary>
public static class SoftDeleteModelBuilderExtensions
{
    /// <summary>Applies <c>HasQueryFilter(e =&gt; e.DeletedAt == null)</c> to every CLR entity type implementing <see cref="ISoftDeletable"/>; call inside <c>OnModelCreating</c> after <c>base.OnModelCreating(modelBuilder)</c>.</summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    public static ModelBuilder ApplySoftDeleteFilter(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(
                Expression.Convert(parameter, typeof(ISoftDeletable)),
                nameof(ISoftDeletable.DeletedAt));
            var filter = Expression.Lambda(Expression.Equal(property, Expression.Constant(null, typeof(DateTimeOffset?))), parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }

        return modelBuilder;
    }
}
