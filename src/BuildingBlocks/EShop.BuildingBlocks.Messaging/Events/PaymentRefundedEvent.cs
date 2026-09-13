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

    /// <summary>
    /// The currency <see cref="Amount"/> was refunded in. Added in Payment audit Stage 8b: until then the event carried a
    /// bare number and Notification's refund email hard-coded USD. Payment charges USD only (its D4). A message published
    /// before the field existed has none, and reads as that default.
    /// </summary>
    public string Currency { get; init; } = "USD";

    public DateTime RefundedAt { get; init; }
}
