using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Command to create an order (typically from BasketCheckedOutEvent).
/// Wrapped in a transaction via ITransactionalCommand.
/// Invalidates user-specific order list cache.
/// </summary>
public record CreateOrderCommand : IRequest<Result<Guid>>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    public List<CreateOrderItemDto> Items { get; init; } = new();
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"orders:user:{UserId}"
    ];
}

public record CreateOrderItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
