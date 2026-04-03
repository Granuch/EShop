using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Basket.Application.Commands.UpdateBasketItemQuantity;

public record UpdateBasketItemQuantityCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"basket:user:{UserId}"
    ];
}
