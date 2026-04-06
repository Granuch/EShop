namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when payment is created and accepted for processing.
/// </summary>
public record PaymentCreatedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public string Status { get; init; } = "PENDING";
    public DateTime CreatedAt { get; init; }
}
