namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when an order is shipped
/// </summary>
public record OrderShippedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public string? TrackingNumber { get; init; }
    public DateTime ShippedAt { get; init; }
}
