using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

/// <summary>
/// Query to get orders for a specific user with distributed caching
/// </summary>
public record GetOrdersByUserQuery : IRequest<Result<List<OrderDto>>>, ICacheableQuery
{
    public string UserId { get; init; } = string.Empty;

    public string CacheKey => $"orders:user:{UserId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(3);
    public TimeSpan? SlidingExpiration => null;
}
