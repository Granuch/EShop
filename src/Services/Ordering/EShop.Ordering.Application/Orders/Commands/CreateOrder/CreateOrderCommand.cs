using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Command to create an order (typically from BasketCheckedOutEvent)
/// </summary>
public record CreateOrderCommand : IRequest<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;
    public List<CreateOrderItemDto> Items { get; init; } = new();
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
}

public record CreateOrderItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
