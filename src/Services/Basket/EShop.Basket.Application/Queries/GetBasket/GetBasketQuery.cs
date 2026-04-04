using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Basket.Application.Queries.GetBasket;

/// <summary>
/// Query to get user's basket
/// </summary>
public record GetBasketQuery : IRequest<Result<BasketDto?>>, ICacheableQuery
{
    public string UserId { get; init; } = string.Empty;

    public string CacheKey => $"basket:user:{UserId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(2);
    public TimeSpan? SlidingExpiration => null;
}
