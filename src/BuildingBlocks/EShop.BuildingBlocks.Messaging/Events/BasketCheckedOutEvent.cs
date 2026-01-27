namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when basket is checked out
/// </summary>
public record BasketCheckedOutEvent : IntegrationEvent
{
    public string UserId { get; init; } = string.Empty;
    public List<CheckoutItem> Items { get; init; } = new();
    public decimal TotalPrice { get; init; }
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
}

public record CheckoutItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
