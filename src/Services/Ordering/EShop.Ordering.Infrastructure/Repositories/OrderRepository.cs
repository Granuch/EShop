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

        return Task.CompletedTask;
    }

    public IQueryable<Order> Query()
    {
        return _context.Orders.AsNoTracking();
    }
}
