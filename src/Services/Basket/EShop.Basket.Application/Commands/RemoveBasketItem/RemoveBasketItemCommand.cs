using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Basket.Application.Commands.RemoveBasketItem;

public record RemoveBasketItemCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    public Guid ProductId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"basket:user:{UserId}"
    ];
}
