namespace EShop.Ordering.Domain.Interfaces;

/// <summary>
/// Reads the product data an order is priced from. Ordering never takes a product's name or price
/// from a client (audit C1): an order created over HTTP, and an item added to one, is priced from
/// Catalog. Declared here rather than in Application because Infrastructure implements it — the
/// same placement as <see cref="IOrderRepository"/>.
/// </summary>
public interface IProductCatalogReader
{
    /// <returns>
    /// The product, or <c>null</c> when Catalog has no orderable product with that id — missing,
    /// deleted and unpublished all read the same to an anonymous caller.
    /// </returns>
    /// <exception cref="CatalogUnavailableException">Catalog could not give an answer.</exception>
    Task<CatalogProduct?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <param name="UnitPrice">
/// The effective price, <c>DiscountPrice ?? Price</c> — the same figure Basket charges, so an order
/// placed directly and one placed through checkout price a product identically.
/// </param>
public sealed record CatalogProduct(Guid ProductId, string Name, decimal UnitPrice);

/// <summary>Catalog was unreachable, timed out, or answered with something other than a product or 404.</summary>
public sealed class CatalogUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
