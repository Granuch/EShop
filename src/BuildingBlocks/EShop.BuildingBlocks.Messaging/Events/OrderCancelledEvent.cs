namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when an order is cancelled
/// </summary>
public record OrderCancelledEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
