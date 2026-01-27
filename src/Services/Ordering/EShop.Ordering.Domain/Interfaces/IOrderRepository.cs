using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Domain.Interfaces;

/// <summary>
/// Repository interface for Order aggregate
/// </summary>
public interface IOrderRepository
{
    // TODO: Implement query methods
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);

    // TODO: Implement CRUD operations
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // TODO: Add pagination support for order history
}
