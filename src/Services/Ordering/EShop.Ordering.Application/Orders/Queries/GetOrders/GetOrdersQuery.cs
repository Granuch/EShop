using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Ordering.Application.Orders.Queries.GetOrders;

/// <summary>
/// Query to get paginated list of all orders (admin)
/// </summary>
public record GetOrdersQuery : IRequest<Result<PagedResult<OrderDto>>>
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public string? Status { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
}
