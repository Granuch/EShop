namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when payment fails
/// </summary>
public record PaymentFailedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; }
}
