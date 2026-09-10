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
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();

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
                .IsConcurrencyToken();

            // Audit fields
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.CreatedBy).HasMaxLength(100);
            entity.Property(p => p.UpdatedAt);
            entity.Property(p => p.UpdatedBy).HasMaxLength(100);

            // The B-tree indexes MUST be declared with an explicit name. An unnamed
            // HasIndex(p => p.Sku) and the trigram HasIndex(p => p.Sku) below are the SAME index
            // builder — EF keys indexes by property set, not by database name — so the later call
            // silently reconfigured this one into a non-unique GIN index. There was no warning and
            // no duplicate-index error: for five migrations `Products.Sku` simply had no unique
            // constraint at all, and `Name` had no B-tree, while a comment here claimed otherwise.
            // Naming them makes them distinct index builders, which is what lets both survive.
            //
            // The Sku index is deliberately PARTIAL. `GetBySkuAsync` runs under the
            // `!p.IsDeleted` global query filter below, so a soft-deleted product's SKU is already
            // invisible to the application's duplicate check; a total unique index would let the
            // database reject a SKU the application had just told the caller was free. Filtering
            // on the same predicate makes the two agree, and makes a soft-deleted product's SKU
            // reusable, which is the intended behaviour.
            entity.HasIndex(p => p.Sku, "IX_Products_Sku")
                .IsUnique()
                .HasFilter("NOT \"IsDeleted\"");
            entity.HasIndex(p => p.Name, "IX_Products_Name");
            entity.HasIndex(p => p.CategoryId);

            // H4. The keyset index for GET /products/newest. (CreatedAt, Id) is exactly the row
            // value the cursor compares against, so a page is one backward range scan from the
            // cursor at any depth. It replaces the single-column IX_Products_CreatedAt, every use
            // of which this one also serves as its leftmost prefix.
            entity.HasIndex(p => new { p.CreatedAt, p.Id }, "IX_Products_CreatedAt_Id");

            // Trigram indexes for ILIKE search performance (requires pg_trgm extension).
            // These stay non-unique: Postgres cannot build a unique GIN index at all, which is why
            // migration 20260217000731_UpdateProductModel2 exists — the fix taken there was to drop
            // the uniqueness rather than to split the indexes, and that is what lost the constraint.
            entity.HasIndex(p => p.Name)
                .HasDatabaseName("IX_Products_Name_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");
            entity.HasIndex(p => p.Sku)
                .HasDatabaseName("IX_Products_Sku_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops")
                .IsUnique(false);

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
                .IsConcurrencyToken();

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

            // The aggregate assigns child ids (Product.AddImage returns the new Guid), so the
            // key is NOT store-generated. Leaving it ValueGeneratedOnAdd makes EF treat an image
            // added to an already-loaded product as an existing row — it issues an UPDATE that
            // matches nothing and throws DbUpdateConcurrencyException.
            entity.Property(pi => pi.Id)
                .ValueGeneratedNever();

            entity.Property(pi => pi.Url)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(pi => pi.AltText)
                .HasMaxLength(200);

            // Replaces the plain ProductId index the FK convention would otherwise add:
            // same database name, widened to cover the "pick the main image" query
            // (§2/§3 of the images plan) as an index-only scan.
            entity.HasIndex(pi => new { pi.ProductId, pi.IsMain, pi.DisplayOrder })
                .HasDatabaseName("IX_ProductImages_ProductId")
                .IncludeProperties(pi => pi.Url);

            // Backstop for the "exactly one main image" invariant the domain already
            // guards (Product/ProductImage internal setters): a filtered unique index
            // permits zero mains (legitimate when a product has no images) but makes two
            // mains impossible even under a write path the aggregate cannot see.
            entity.HasIndex(pi => pi.ProductId)
                .HasDatabaseName("IX_ProductImages_ProductId_IsMain")
                .IsUnique()
                .HasFilter("\"IsMain\"");
        });

        modelBuilder.Entity<ProductAttribute>(entity =>
        {
            entity.ToTable("ProductAttributes");

            entity.HasKey(pa => pa.Id);

            // Domain-assigned key — see the note on ProductImage.Id above.
            entity.Property(pa => pa.Id)
                .ValueGeneratedNever();

            entity.Property(pa => pa.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(pa => pa.Value)
                .IsRequired()
                .HasMaxLength(200);
        });

        base.OnModelCreating(modelBuilder);
    }

}
