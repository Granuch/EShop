using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Domain.Interfaces;

/// <summary>
/// Repository interface for the Order aggregate — writes and the loads they need. Reads for display go
/// through <c>IOrderQueryService</c>. <c>GetByIdReadOnlyAsync</c>, <c>GetByUserIdAsync</c>,
/// <c>GetByStatusAsync</c> and <c>Query()</c> were removed in audit L5/L11: the first was replaced by the
/// query service's projection, and the other three had no callers.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The owning user's id, or <c>null</c> when the order does not exist. Reads one column.</summary>
    Task<string?> GetOwnerIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many notes the order already carries (Admin panel S9). On the write side rather than in
    /// <c>IOrderQueryService</c> because its only caller is the <see cref="Order.MaxNotes"/> pre-check:
    /// the aggregate cannot count children it never loads, so this is the only place the real number is
    /// visible before a note is appended.
    /// </summary>
    Task<int> CountNotesAsync(Guid orderId, CancellationToken cancellationToken = default);
}
