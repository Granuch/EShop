using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Events;

/// <summary>
/// Event raised when product goes out of stock
/// </summary>
public record ProductOutOfStockEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid ProductId { get; init; }
}
