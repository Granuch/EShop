namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when payment processing completes successfully.
/// </summary>
public record PaymentCompletedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public string PaymentIntentId { get; init; } = string.Empty;
    public DateTime CompletedAt { get; init; }
}
