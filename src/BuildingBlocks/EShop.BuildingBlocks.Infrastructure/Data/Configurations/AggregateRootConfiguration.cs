using EShop.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EShop.BuildingBlocks.Infrastructure.Data.Configurations;

/// <summary>
/// Base EF Core configuration for all aggregate roots.
/// Configures:
/// - Version as a concurrency token for optimistic locking
/// - Audit fields (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
/// 
/// Usage: Inherit from this class in your entity configurations.
/// </summary>
public abstract class AggregateRootConfiguration<TEntity, TId> : IEntityTypeConfiguration<TEntity>
    where TEntity : AggregateRoot<TId>
{
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        // Configure Version as a concurrency token
        // EF Core will include this in the WHERE clause of UPDATE/DELETE statements
        // If the version doesn't match, DbUpdateConcurrencyException is thrown
        builder.Property(e => e.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        // Configure audit fields
        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.CreatedBy)
            .HasMaxLength(100);

        builder.Property(e => e.UpdatedAt);

        builder.Property(e => e.UpdatedBy)
            .HasMaxLength(100);

        // Index on CreatedAt for time-based queries
        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName($"IX_{typeof(TEntity).Name}_CreatedAt");
    }
}

/// <summary>
/// Extension methods for configuring aggregate roots in OnModelCreating.
/// </summary>
public static class AggregateRootConfigurationExtensions
{
    /// <summary>
    /// Configures optimistic concurrency for an aggregate root.
    /// Call this for any entity that inherits from AggregateRoot.
    /// </summary>
    public static void ConfigureAggregateRoot<TEntity, TId>(
        this ModelBuilder modelBuilder)
        where TEntity : AggregateRoot<TId>
    {
        modelBuilder.Entity<TEntity>(builder =>
        {
            builder.Property(e => e.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(0);

            builder.Property(e => e.CreatedAt)
                .IsRequired();

            builder.Property(e => e.CreatedBy)
                .HasMaxLength(100);

            builder.Property(e => e.UpdatedAt);

            builder.Property(e => e.UpdatedBy)
                .HasMaxLength(100);
        });
    }
}
