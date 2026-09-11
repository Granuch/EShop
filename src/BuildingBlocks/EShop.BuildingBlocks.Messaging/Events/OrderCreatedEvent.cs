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

    /// <summary>
    /// Every line of the order. Filled since Ordering audit Stage 8; before that it was always empty,
    /// so a consumer counting it (Notification's order-confirmation item count) always saw zero.
    /// </summary>
    public List<OrderEventItem> Items { get; init; } = new();
}

public record OrderEventItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;

    /// <summary>The unit price the order was placed at, as stored on the order line.</summary>
    public decimal Price { get; init; }

    public int Quantity { get; init; }

    /// <summary><see cref="Price"/> × <see cref="Quantity"/>.</summary>
    public decimal SubTotal { get; init; }
}
