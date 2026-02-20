namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a product's price changes.
/// </summary>
public record ProductPriceChangedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
    public decimal OldPrice { get; init; }
    public decimal NewPrice { get; init; }
}
