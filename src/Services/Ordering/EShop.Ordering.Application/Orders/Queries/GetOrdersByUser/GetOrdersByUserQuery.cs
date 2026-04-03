using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

/// <summary>
/// Query to get paginated orders for a specific user with distributed caching.
/// </summary>
public record GetOrdersByUserQuery : IRequest<Result<PagedResult<OrderDto>>>, ICacheableQuery
{
    public string UserId { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    /// <summary>
    /// Optional cursor for keyset pagination (CreatedAt value of the last order on previous page).
    /// </summary>
    public DateTime? Cursor { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;

    public string CacheKey =>
        $"orders:user:{UserId}:p={EffectivePageNumber}:ps={EffectivePageSize}:cur={Cursor?.Ticks}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(3);
    public TimeSpan? SlidingExpiration => null;
}
