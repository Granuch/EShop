using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderById;

/// <summary>
/// Query to get an order by ID with automatic distributed caching
/// </summary>
public record GetOrderByIdQuery : IRequest<Result<OrderDto>>, ICacheableQuery
{
    public Guid OrderId { get; init; }

    public string CacheKey => $"order:{OrderId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
