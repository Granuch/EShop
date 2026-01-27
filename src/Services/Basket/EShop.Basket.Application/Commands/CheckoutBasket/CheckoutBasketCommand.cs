using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Command to checkout basket and create order
/// </summary>
public record CheckoutBasketCommand : IRequest<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
}
