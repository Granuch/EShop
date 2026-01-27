using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Events;

/// <summary>
/// Event raised when a product is created
/// </summary>
public record ProductCreatedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
}
