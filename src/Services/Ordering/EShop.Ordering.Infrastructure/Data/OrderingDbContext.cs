using Microsoft.EntityFrameworkCore;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Infrastructure.Data;

/// <summary>
/// DbContext for Ordering service
/// </summary>
public class OrderingDbContext : BaseDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TODO: Configure Order aggregate
        // modelBuilder.Entity<Order>(entity => { ... });

        // TODO: Configure OrderItem with owned entity relationship
        // modelBuilder.Entity<OrderItem>(entity => { ... });

        // TODO: Configure Address as owned type
        // modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress);

        // TODO: Add indexes for UserId, Status, CreatedAt
        // TODO: Implement outbox pattern table for reliable messaging

        base.OnModelCreating(modelBuilder);
    }
}
