using EShop.BuildingBlocks.Domain;

namespace EShop.Basket.Domain.Events;

/// <summary>
/// Domain event raised when a shopping basket is checked out.
/// </summary>
public record BasketCheckedOutDomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public string UserId { get; init; } = string.Empty;
    public IReadOnlyCollection<BasketCheckedOutDomainEventItem> Items { get; init; } = [];
    public decimal TotalPrice { get; init; }
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
}

public record BasketCheckedOutDomainEventItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
