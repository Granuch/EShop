using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Events;

/// <summary>
/// Event raised when order is created
/// </summary>
public record OrderCreatedDomainEvent : IDomainEvent
{
    // init, not get-only, on every Ordering domain event (audit L3). The outbox stores the event as
    // JSON and the processor deserializes it before any handler runs; System.Text.Json cannot set a
    // get-only property, so these initializers ran again on the way back and OccurredOn became the
    // processor's clock — up to ~30 s late at idle back-off.
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// The order's lines, for <c>OrderCreatedEvent.Items</c> (audit M5). That field was never filled,
    /// so every order-confirmation email reported zero items.
    /// </summary>
    public List<OrderCreatedLine> Items { get; init; } = [];
}

/// <summary>One line of a newly created order, as carried by <see cref="OrderCreatedDomainEvent"/>.</summary>
public sealed record OrderCreatedLine
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}
