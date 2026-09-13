namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// The payment for an order was captured. Ordering consumes it to mark the order Paid.
/// <para>Payment audit Stage 8 (M5). This is the same fact as <see cref="PaymentCompletedEvent"/>, which Notification
/// consumes. Payment always publishes the two together, built from the same payment record, so they agree on the amount,
/// currency, intent and time.</para>
/// </summary>
public record PaymentSuccessEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string PaymentIntentId { get; init; } = string.Empty;
    public decimal Amount { get; init; }

    /// <summary>
    /// The currency <see cref="Amount"/> was charged in. Added in Payment audit Stage 8: until then the event carried a
    /// bare number, so Ordering could not tell 100 JPY from 100 USD (C2). Payment charges USD only (its D4). A message
    /// published before the field existed has none, and reads as that default.
    /// </summary>
    public string Currency { get; init; } = "USD";

    public DateTime ProcessedAt { get; init; }
}
