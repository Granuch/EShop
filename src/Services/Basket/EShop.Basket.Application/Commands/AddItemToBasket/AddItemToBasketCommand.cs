using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Basket.Application.Commands.AddItemToBasket;

/// <summary>
/// Command to add item to basket
/// </summary>
public record AddItemToBasketCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"basket:user:{UserId}"
    ];
}
