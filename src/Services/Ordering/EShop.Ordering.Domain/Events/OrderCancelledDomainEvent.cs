using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Events;

/// <summary>
/// Event raised when an order is cancelled
/// </summary>
public record OrderCancelledDomainEvent : IDomainEvent
{
    // init so they survive the outbox round trip — see OrderCreatedDomainEvent.
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
