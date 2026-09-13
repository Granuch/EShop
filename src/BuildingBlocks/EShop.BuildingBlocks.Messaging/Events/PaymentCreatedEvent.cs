namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// A payment attempt has started, and nothing has been charged yet. The simulator is about to settle the payment, or the
/// customer has been given a Stripe intent to pay. Sent once per payment. Payment audit Stage 8 (M5): the outcome follows
/// as <see cref="PaymentSuccessEvent"/>/<see cref="PaymentCompletedEvent"/> or <see cref="PaymentFailedEvent"/>, and
/// a Stripe customer may also never pay. So a consumer must not read this event as money received.
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
