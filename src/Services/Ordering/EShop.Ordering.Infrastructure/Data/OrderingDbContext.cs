using Microsoft.EntityFrameworkCore;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Ordering.Domain.Entities;
using MediatR;

namespace EShop.Ordering.Infrastructure.Data;

/// <summary>
/// DbContext for Ordering service.
/// Inherits from BaseDbContext to get UnitOfWork, domain events, outbox, and audit field support.
/// </summary>
public class OrderingDbContext : BaseDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderNote> OrderNotes => Set<OrderNote>();
    /// <summary>
    /// Deliberately not named <c>OrderStatusHistory</c>: a member with the same name as its element
    /// type shadows the type inside this class, and <c>OrderStatusHistory.MaxReasonLength</c> below
    /// then fails to compile with an error about <c>DbSet</c> having no such member.
    /// </summary>
    public DbSet<OrderStatusHistory> OrderStatusHistoryEntries => Set<OrderStatusHistory>();

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options)
    {
    }

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options, IMediator mediator)
        : base(options, mediator)
    {
    }

    public OrderingDbContext(
        DbContextOptions<OrderingDbContext> options,
        IMediator mediator,
        ICurrentUserContext currentUserContext)
        : base(options, mediator, currentUserContext)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");

            entity.HasKey(o => o.Id);

            entity.Property(o => o.UserId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(o => o.TotalPrice)
                .HasColumnType("decimal(18,2)");

            entity.Property(o => o.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(o => o.PaymentIntentId)
                .HasMaxLength(200);

            entity.Property(o => o.CancellationReason)
                .HasMaxLength(500);

            // Optimistic concurrency token
            entity.Property(o => o.Version)
                .IsConcurrencyToken();

            // Audit fields
            entity.Property(o => o.CreatedAt).IsRequired();
            entity.Property(o => o.CreatedBy).HasMaxLength(100);
            entity.Property(o => o.UpdatedAt);
            entity.Property(o => o.UpdatedBy).HasMaxLength(100);

            // Address as owned type
            entity.OwnsOne(o => o.ShippingAddress, address =>
            {
                address.Property(a => a.Street).HasMaxLength(200).IsRequired().HasColumnName("ShippingStreet");
                address.Property(a => a.City).HasMaxLength(100).IsRequired().HasColumnName("ShippingCity");
                address.Property(a => a.State).HasMaxLength(100).HasColumnName("ShippingState");
                address.Property(a => a.ZipCode).HasMaxLength(20).HasColumnName("ShippingZipCode");
                address.Property(a => a.Country).HasMaxLength(100).IsRequired().HasColumnName("ShippingCountry");
            });

            // Navigation to items
            entity.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Admin panel S9. Both are append-only children the aggregate writes and never reads back:
            // GetByIdAsync does not include them, so on a loaded order the collections hold only what
            // the current operation added. The read path is IOrderQueryService.
            entity.HasMany(o => o.StatusHistory)
                .WithOne()
                .HasForeignKey(h => h.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(o => o.Notes)
                .WithOne()
                .HasForeignKey(n => n.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            // Audit L12. The per-user list filters on UserId and orders by (CreatedAt, Id) descending; this
            // one index serves both and replaces the single-column UserId index, whose lookups it covers.
            entity.HasIndex(o => new { o.UserId, o.CreatedAt, o.Id })
                .IsDescending(false, true, true);
            // Admin panel S8 (M6). The admin list filters on Status and orders by (CreatedAt, Id)
            // descending, so this serves the filtered list the way the L12 index serves the per-user
            // one — and it replaces the single-column Status index, whose lookups it covers as a
            // leading-column prefix. Same reasoning, same shape, one index fewer to maintain.
            entity.HasIndex(o => new { o.Status, o.CreatedAt, o.Id })
                .IsDescending(false, true, true);
            // The UNfiltered admin list orders by the same pair, so this one keeps its job.
            entity.HasIndex(o => o.CreatedAt);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");

            entity.HasKey(i => i.Id);

            // The aggregate assigns child ids (OrderItem's constructor sets Id = Guid.NewGuid()),
            // so the key is NOT store-generated. Leaving it ValueGeneratedOnAdd makes EF treat an
            // item added to an already-loaded order as an existing row — it issues an UPDATE that
            // matches nothing and throws DbUpdateConcurrencyException. Same reasoning as
            // CatalogDbContext's ProductImage/ProductAttribute keys.
            entity.Property(i => i.Id)
                .ValueGeneratedNever();

            entity.Property(i => i.ProductId).IsRequired();

            entity.Property(i => i.ProductName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(i => i.UnitPrice)
                .HasColumnType("decimal(18,2)");

            entity.Property(i => i.Quantity)
                .IsRequired();

            // Audit fields
            entity.Property(i => i.CreatedAt).IsRequired();
            entity.Property(i => i.CreatedBy).HasMaxLength(100);
            entity.Property(i => i.UpdatedAt);
            entity.Property(i => i.UpdatedBy).HasMaxLength(100);

            // Indexes
            entity.HasIndex(i => i.OrderId);
            entity.HasIndex(i => i.ProductId);
        });

        // Admin panel S9 (M5). The order's timeline: one row per state transition, written by the
        // aggregate inside the transition's own SaveChanges.
        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("OrderStatusHistory");

            entity.HasKey(h => h.Id);

            // Same reasoning as OrderItem above, and the reason it matters more here: every row is
            // added to an order that is ALREADY persisted (a ship, a deliver, a cancel), which is
            // precisely the case ValueGeneratedOnAdd turns into an UPDATE that matches nothing and a
            // DbUpdateConcurrencyException — BUG-02's shape.
            entity.Property(h => h.Id)
                .ValueGeneratedNever();

            entity.Property(h => h.OrderId).IsRequired();

            // Stored as names, like Order.Status, so a row stays readable after the enum is reordered.
            entity.Property(h => h.FromStatus)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(h => h.ToStatus)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(h => h.Reason)
                .HasMaxLength(OrderStatusHistory.MaxReasonLength);

            entity.Property(h => h.OccurredAt).IsRequired();

            // Audit fields. CreatedBy is the actor: BaseDbContext.SetAuditFields stamps it from
            // ICurrentUserContext, so an admin's ship lands their id and a bus-driven transition lands
            // "system" — without threading an actor parameter through every domain method.
            entity.Property(h => h.CreatedAt).IsRequired();
            entity.Property(h => h.CreatedBy).HasMaxLength(100);
            entity.Property(h => h.UpdatedAt);
            entity.Property(h => h.UpdatedBy).HasMaxLength(100);

            // The only query: one order's timeline, oldest first.
            entity.HasIndex(h => new { h.OrderId, h.OccurredAt });
        });

        // Admin panel S9 (M4). Operator notes, append-only and admin-only.
        modelBuilder.Entity<OrderNote>(entity =>
        {
            entity.ToTable("OrderNotes");

            entity.HasKey(n => n.Id);

            entity.Property(n => n.Id)
                .ValueGeneratedNever();

            entity.Property(n => n.OrderId).IsRequired();

            entity.Property(n => n.AuthorId)
                .IsRequired()
                .HasMaxLength(OrderNote.MaxAuthorIdLength);

            entity.Property(n => n.AuthorName)
                .IsRequired()
                .HasMaxLength(OrderNote.MaxAuthorNameLength);

            entity.Property(n => n.Body)
                .IsRequired()
                .HasMaxLength(OrderNote.MaxBodyLength);

            entity.Property(n => n.CreatedAt).IsRequired();
            entity.Property(n => n.CreatedBy).HasMaxLength(100);
            entity.Property(n => n.UpdatedAt);
            entity.Property(n => n.UpdatedBy).HasMaxLength(100);

            // One order's notes, newest first, and the COUNT the cap pre-check runs.
            entity.HasIndex(n => new { n.OrderId, n.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
