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
}
