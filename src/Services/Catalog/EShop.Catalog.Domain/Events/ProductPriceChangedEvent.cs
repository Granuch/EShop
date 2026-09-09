using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Events;

/// <summary>
/// Raised when the price a customer would pay for a product changes.
///
/// <para>
/// <b><see cref="OldPrice"/> and <see cref="NewPrice"/> are effective prices</b> —
/// <c>DiscountPrice ?? Price</c>, not the list price. Both a list-price change and a
/// discount being applied or ended can move that value, and Basket's consumer writes
/// <see cref="NewPrice"/> straight onto a basket item whose price came from the effective
/// price at add time; publishing the list price here would reprice a discounted item up to
/// full price. See <c>Product.UpdatePrice</c>.
/// </para>
/// </summary>
public record ProductPriceChangedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid ProductId { get; init; }
    public decimal OldPrice { get; init; }
    public decimal NewPrice { get; init; }
}
