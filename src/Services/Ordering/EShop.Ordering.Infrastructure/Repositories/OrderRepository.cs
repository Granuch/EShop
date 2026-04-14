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

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Legacy read method retained for backward compatibility.
        // User-facing queries should use IOrderQueryService.GetOrdersByUserAsync for pagination.
        return await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == status)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Orders.AddAsync(order, cancellationToken);
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var trackedOrder = _context.Entry(order);
        if (trackedOrder.State == EntityState.Detached)
        {
            _context.Attach(order);
        }

        _context.ChangeTracker.DetectChanges();

        var persistedItemIds = await _context.OrderItems
            .AsNoTracking()
            .Where(i => i.OrderId == order.Id)
            .Select(i => i.Id)
            .ToHashSetAsync(cancellationToken);

        // Ensure newly added items are marked as Added for insert.
        foreach (var item in order.Items)
        {
            var itemEntry = _context.Entry(item);
            if (itemEntry.State == EntityState.Detached)
            {
                _context.Attach(item);
                itemEntry = _context.Entry(item);
            }

            if (!persistedItemIds.Contains(item.Id)
                && (itemEntry.State == EntityState.Modified || itemEntry.State == EntityState.Unchanged))
            {
                itemEntry.State = EntityState.Added;
            }
        }
    }

    public IQueryable<Order> Query()
    {
        return _context.Orders.AsNoTracking();
    }
}
