namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Published when an order's total changes after it was created: an item was added, removed or given a new quantity
/// while the order was still Pending (frontend-contracts F-47).
///
/// <para>
/// Payment records the amount to charge once, from <see cref="OrderCreatedEvent.TotalAmount"/>. Without this event it
/// never learned about a later change, so it charged the old total and Ordering then refused the success because the
/// amounts differed, leaving a paid order Pending for good. Payment's <c>OrderTotalChangedConsumer</c> brings its
/// recorded amount — and a Stripe intent already created for it — up to <see cref="NewTotal"/>.
/// </para>
/// </summary>
public record OrderTotalChangedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;

    /// <summary>The order's whole total after the change, not a delta: a consumer never has to add anything up.</summary>
    public decimal NewTotal { get; init; }

    /// <summary>The currency <see cref="NewTotal"/> is in. Ordering prices every order in USD.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// When the order's total became <see cref="NewTotal"/>: the instant of the change in Ordering, not of publishing.
    /// Two changes can be consumed out of order (concurrent consumers, a retried message), so a consumer applies a total
    /// only if it is newer than the last one it applied, and this is what it compares.
    /// </summary>
    public DateTime TotalAsOf { get; init; }
}
