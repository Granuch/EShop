using EShop.BuildingBlocks.Application;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;

namespace EShop.Ordering.Application.Orders;

/// <summary>
/// Turns (product, quantity) requests into priced order items using Catalog — never the caller.
/// Audit C1: POST /orders and POST /orders/{id}/items used to take each item's name and price from
/// the request body, and Payment charged whatever total that produced.
/// </summary>
public static class CatalogPricing
{
    /// <summary>Mapped to 503: the request may well be valid, it just cannot be priced right now.</summary>
    public static readonly Error CatalogUnavailable = new(
        "Catalog.Unavailable",
        "The product catalog could not be reached to price this order. Try again shortly.");

    public static Error ProductUnavailable(Guid productId) => new(
        "Order.ProductUnavailable",
        $"Product '{productId}' does not exist or is not available to order.");

    public static async Task<Result<IReadOnlyList<OrderItem>>> PriceAsync(
        IProductCatalogReader catalog,
        IEnumerable<(Guid ProductId, int Quantity)> lines,
        CancellationToken cancellationToken)
    {
        var items = new List<OrderItem>();

        foreach (var (productId, quantity) in lines)
        {
            CatalogProduct? product;
            try
            {
                product = await catalog.GetByIdAsync(productId, cancellationToken);
            }
            catch (CatalogUnavailableException)
            {
                return Result<IReadOnlyList<OrderItem>>.Failure(CatalogUnavailable);
            }

            if (product is null)
            {
                return Result<IReadOnlyList<OrderItem>>.Failure(ProductUnavailable(productId));
            }

            // The requested id, not the payload's: the reader already rejects a payload for another
            // product, and this keeps the order line tied to what the caller asked for.
            items.Add(new OrderItem(productId, product.Name, product.UnitPrice, quantity));
        }

        return Result<IReadOnlyList<OrderItem>>.Success(items);
    }
}
