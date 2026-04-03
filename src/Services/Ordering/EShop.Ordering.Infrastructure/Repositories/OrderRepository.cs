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
        // EF Core detects scalar property changes on tracked entities automatically.
        // However, new child entities added to a tracked parent's navigation via domain
        // methods get a non-default key (Guid.NewGuid() in constructor), which causes
        // EF Core's DetectChanges to treat them as Modified (existing) instead of Added.
        //
        // Fix: capture IDs of items already tracked (loaded from DB) BEFORE DetectChanges
        // runs. Then trigger DetectChanges manually and fix up any new items that were
        // incorrectly marked as Modified.
        var loadedItemIds = _context.ChangeTracker.Entries<OrderItem>()
            .Where(e => e.State != EntityState.Added && e.State != EntityState.Detached)
            .Select(e => e.Entity.Id)
            .ToHashSet();

        // Force EF to discover the new items in the navigation collection
        _context.ChangeTracker.DetectChanges();

        // Any item not in the original loaded set is new — mark as Added
        foreach (var item in order.Items)
        {
            if (!loadedItemIds.Contains(item.Id))
            {
                _context.Entry(item).State = EntityState.Added;
            }
        }

        return Task.CompletedTask;
    }

    public IQueryable<Order> Query()
    {
        return _context.Orders.AsNoTracking();
    }
}
