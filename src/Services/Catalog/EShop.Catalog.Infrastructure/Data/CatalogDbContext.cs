using Microsoft.EntityFrameworkCore;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Infrastructure.Data;

/// <summary>
/// DbContext for Catalog service
/// </summary>
public class CatalogDbContext : BaseDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TODO: Configure Product entity (table name, indexes, relationships)
        // modelBuilder.Entity<Product>(entity => { ... });

        // TODO: Configure Category entity with self-referencing relationship
        // modelBuilder.Entity<Category>(entity => { ... });

        // TODO: Add indexes for SKU, Name, CategoryId
        // TODO: Add soft delete query filter
        // TODO: Seed sample data

        base.OnModelCreating(modelBuilder);
    }
}
