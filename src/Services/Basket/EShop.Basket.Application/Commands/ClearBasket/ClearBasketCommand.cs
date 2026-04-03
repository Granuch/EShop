using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Basket.Application.Commands.ClearBasket;

public record ClearBasketCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"basket:user:{UserId}"
    ];
}
