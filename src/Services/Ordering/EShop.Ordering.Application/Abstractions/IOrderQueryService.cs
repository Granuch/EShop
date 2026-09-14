using EShop.Ordering.Application.Orders.Queries;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Abstractions;

/// <summary>
/// Query service for order read operations with filtering and pagination.
/// Implemented in Infrastructure to keep EF Core-specific query logic
/// out of the Application layer.
/// </summary>
public interface IOrderQueryService
{
    Task<(List<OrderDto> Items, int TotalCount)> GetOrdersAsync(
        OrderStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>One order, through the same projection as the lists (audit L5), or <c>null</c>.</summary>
    Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<(List<OrderDto> Items, int TotalCount)> GetOrdersByUserAsync(
        string userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
