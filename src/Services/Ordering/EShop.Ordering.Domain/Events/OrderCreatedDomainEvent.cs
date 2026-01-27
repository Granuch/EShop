using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Events;

/// <summary>
/// Event raised when order is created
/// </summary>
public record OrderCreatedDomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}
