namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when payment refund succeeds
/// </summary>
public record PaymentRefundedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string PaymentIntentId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime RefundedAt { get; init; }
}
