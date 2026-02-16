using Microsoft.EntityFrameworkCore;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Catalog.Domain.Entities;
using MediatR;

namespace EShop.Catalog.Infrastructure.Data;

/// <summary>
/// DbContext for Catalog service.
/// Inherits from BaseDbContext to get UnitOfWork, domain events, outbox, and audit field support.
/// </summary>
public class CatalogDbContext : BaseDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, IMediator mediator)
        : base(options, mediator)
    {
    }

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        IMediator mediator,
        ICurrentUserContext currentUserContext)
        : base(options, mediator, currentUserContext)
    {
    }

   protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enable pg_trgm extension for trigram-based search indexes
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");

            entity.HasKey(p => p.Id);

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(p => p.Sku)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(p => p.Price)
                .HasColumnType("decimal(18,2)");

            entity.Property(p => p.DiscountPrice)
                .HasColumnType("decimal(18,2)");

            // Optimistic concurrency token
            entity.Property(p => p.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(0);

            // Audit fields
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.CreatedBy).HasMaxLength(100);
            entity.Property(p => p.UpdatedAt);
            entity.Property(p => p.UpdatedBy).HasMaxLength(100);

            entity.HasIndex(p => p.Sku).IsUnique();
            entity.HasIndex(p => p.Name);
            entity.HasIndex(p => p.CategoryId);
            entity.HasIndex(p => p.CreatedAt);

            // Trigram indexes for ILIKE search performance (requires pg_trgm extension)
            entity.HasIndex(p => p.Name)
                .HasDatabaseName("IX_Products_Name_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");
            entity.HasIndex(p => p.Sku)
                .HasDatabaseName("IX_Products_Sku_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.HasMany(p => p.Images)
                .WithOne()
                .HasForeignKey(pi => pi.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.Attributes)
                .WithOne()
                .HasForeignKey(pa => pa.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(c => c.Slug)
                .IsRequired()
                .HasMaxLength(200);

            // Optimistic concurrency token
            entity.Property(c => c.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(0);

            // Audit fields
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.CreatedBy).HasMaxLength(100);
            entity.Property(c => c.UpdatedAt);
            entity.Property(c => c.UpdatedBy).HasMaxLength(100);

            entity.HasOne(c => c.ParentCategory)
                .WithMany(c => c.ChildCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(c => new { c.ParentCategoryId, c.Slug }).IsUnique();
            entity.HasIndex(c => c.Slug)
                .IsUnique()
                .HasFilter("\"ParentCategoryId\" IS NULL");
            entity.HasIndex(c => c.CreatedAt);

            entity.HasQueryFilter(c => c.IsActive);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("ProductImages");

            entity.HasKey(pi => pi.Id);

            entity.Property(pi => pi.Url)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(pi => pi.AltText)
                .HasMaxLength(200);

            entity.HasIndex(pi => pi.ProductId);
        });

        base.OnModelCreating(modelBuilder);
    }

}
