using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Events;

public record ProductBackInStockEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid ProductId { get; init; }
}