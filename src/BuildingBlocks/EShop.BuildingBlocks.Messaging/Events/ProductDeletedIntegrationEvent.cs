namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a product is deleted (soft-deleted).
/// </summary>
public record ProductDeletedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
}
