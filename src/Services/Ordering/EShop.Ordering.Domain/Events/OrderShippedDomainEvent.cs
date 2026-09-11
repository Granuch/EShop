using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Events;

/// <summary>
/// Event raised when an order is shipped
/// </summary>
public record OrderShippedDomainEvent : IDomainEvent
{
    // init so they survive the outbox round trip — see OrderCreatedDomainEvent. OccurredOn is what
    // OrderShippedEvent.ShippedAt is taken from, so it was the ship time only before serialization.
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
}
