using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Events;

/// <summary>
/// Raised when an item change moves a Pending order's total (frontend-contracts F-47). Payment has to hear about it:
/// it charges the amount it recorded from <see cref="OrderCreatedDomainEvent"/>, and <c>Order.MarkAsPaid</c> refuses
/// a payment that does not match the current total.
/// </summary>
public record OrderTotalChangedDomainEvent : IDomainEvent
{
    // init so they survive the outbox round trip — see OrderCreatedDomainEvent.
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>The instant of the change; the integration event carries it as <c>TotalAsOf</c>.</summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal NewTotal { get; init; }
}
