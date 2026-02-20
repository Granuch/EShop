namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a new product is created in the catalog.
/// </summary>
public record ProductCreatedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public Guid CategoryId { get; init; }
}
