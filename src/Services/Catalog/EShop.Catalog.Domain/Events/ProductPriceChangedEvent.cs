using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Events;

/// <summary>
/// Event raised when product price changes
/// </summary>
public record ProductPriceChangedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid ProductId { get; init; }
    public decimal OldPrice { get; init; }
    public decimal NewPrice { get; init; }
}
