namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when the price a customer would pay for a product changes.
///
/// <para>
/// <b><see cref="OldPrice"/> and <see cref="NewPrice"/> are effective prices</b> — the product's
/// discount price when one is active, otherwise its list price. Catalog raises this for a
/// list-price change <i>and</i> for a discount being applied or ended, and only when that
/// customer-facing value actually moves. Basket's <c>ProductPriceChangedConsumer</c> assigns
/// <see cref="NewPrice"/> directly to basket items, which were priced from the same effective
/// value when they were added; a consumer that needs the list price must ask Catalog for it.
/// </para>
/// </summary>
public record ProductPriceChangedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
    public decimal OldPrice { get; init; }
    public decimal NewPrice { get; init; }
}
