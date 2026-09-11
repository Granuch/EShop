using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

/// <summary>
/// Query to get paginated orders for a specific user with distributed caching.
/// Versioned by <see cref="OrderCacheKeys.UserOrders"/>: every page of one user's list is invalidated
/// together by bumping that family, since no write can name the pages.
/// </summary>
public record GetOrdersByUserQuery : IRequest<Result<PagedResult<OrderDto>>>, ICacheableQuery, IVersionedCacheKey
{
    public string UserId { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    /// <summary>
    /// <b>Removed; any value is rejected</b> by the validator (audit M4). Cursor mode compared
    /// <c>CreatedAt &lt; cursor</c> alone, so it skipped every order sharing the boundary timestamp,
    /// reported offset-shaped <c>totalPages</c>/<c>hasNextPage</c>, and never issued a next cursor.
    /// It stays bound, as a string so that any value reaches the validator, because an unknown query
    /// parameter is ignored: dropping it would answer a cursor request with page one and a 200, the
    /// silent restart a client paging by cursor can never detect.
    /// </summary>
    public string? Cursor { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;

    public string CacheKey =>
        $"orders:user:{UserId}:p={EffectivePageNumber}:ps={EffectivePageSize}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(3);
    public TimeSpan? SlidingExpiration => null;

    public string CacheKeyFamily => OrderCacheKeys.UserOrders(UserId);
}
