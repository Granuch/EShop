using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Domain.Interfaces;

/// <summary>
/// Repository interface for Order aggregate
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    IQueryable<Order> Query();
}
