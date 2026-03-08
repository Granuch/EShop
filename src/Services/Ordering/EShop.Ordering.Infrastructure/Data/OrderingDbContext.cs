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
                .IsConcurrencyToken()
                .HasDefaultValue(0);

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

            // Indexes
            entity.HasIndex(o => o.UserId);
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => o.CreatedAt);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");

            entity.HasKey(i => i.Id);

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

        base.OnModelCreating(modelBuilder);
    }
}
