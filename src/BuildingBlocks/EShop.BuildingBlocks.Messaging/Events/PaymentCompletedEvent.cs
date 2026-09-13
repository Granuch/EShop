namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// The payment for an order was captured. Notification consumes it to tell the customer.
/// <para>Payment audit Stage 8 (M5). This is the same fact as <see cref="PaymentSuccessEvent"/>, which Ordering
/// consumes. The two are always published together from one payment record. Both were kept rather than merged,
/// because removing either strands a queue and any outbox row still holding it (Payment D9).</para>
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
