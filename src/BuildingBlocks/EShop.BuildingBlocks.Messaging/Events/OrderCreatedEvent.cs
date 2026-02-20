namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when an order is created.
/// Note: UserEmail intentionally omitted to avoid PII in message payloads.
/// Notification service should look up email via Identity API using UserId.
/// </summary>
public record OrderCreatedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public List<OrderEventItem> Items { get; init; } = new();
}

public record OrderEventItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public decimal SubTotal { get; init; }
}
