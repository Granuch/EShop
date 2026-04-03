using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Command to checkout basket and create order
/// </summary>
public record CheckoutBasketCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"basket:user:{UserId}"
    ];
}
