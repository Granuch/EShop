using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Ordering.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly OrderingDbContext _context;

    public OrderRepository(OrderingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// One query. <c>AsSplitQuery</c> exists to avoid the row explosion of several collection includes;
    /// with a single collection (the items) it only added a second round trip (audit L11).
    /// </summary>
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public Task<string?> GetOwnerIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => o.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Orders.AddAsync(order, cancellationToken);
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var trackedOrder = _context.Entry(order);
        if (trackedOrder.State == EntityState.Detached)
        {
            _context.Attach(order);
        }

        // OrderItem.Id is mapped ValueGeneratedNever (see OrderingDbContext), so EF marks a
        // newly added child under a loaded parent as Added on its own. No manual state fixup
        // and no extra round-trip to fetch persisted item ids are needed here.
        _context.ChangeTracker.DetectChanges();

        // Ordering audit L4. Version, the concurrency token, is bumped only when the Orders row itself is
        // Modified (BaseDbContext.SetAuditFields). A change to the items alone that leaves every order
        // column as it was — a zero-price line keeps the total — wrote no Orders row and so was checked
        // against nothing: two concurrent adds of the same product could both commit, breaking the
        // one-line-per-product rule. Any change to one of this order's items now marks the order
        // Modified, so its Version is bumped and checked.
        var orderEntry = _context.Entry(order);
        if (orderEntry.State == EntityState.Unchanged
            && _context.ChangeTracker.Entries<OrderItem>().Any(item => BelongsTo(item, order.Id)
                && item.State is EntityState.Added or EntityState.Deleted or EntityState.Modified))
        {
            orderEntry.State = EntityState.Modified;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the foreign key through EF so it works whether <c>OrderId</c> is a CLR or a shadow property.
    /// A removed item may keep the key only as its original value.
    /// </summary>
    private static bool BelongsTo(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<OrderItem> item, Guid orderId)
        => Equals(item.Property("OrderId").CurrentValue, orderId)
           || Equals(item.Property("OrderId").OriginalValue, orderId);
}
