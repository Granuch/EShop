namespace EShop.Basket.Application.Abstractions;

public interface IProductCatalogReader
{
    /// <summary>The product as the public catalog shows it, or null if it is missing or not published.</summary>
    Task<ProductCatalogSnapshot?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// What Basket needs to know about a product. <paramref name="Price"/> is the effective price
/// (<c>DiscountPrice ?? Price</c>); <paramref name="StockQuantity"/> is what Catalog reports in stock, checked when an
/// item is added and again at checkout (Basket audit S6). <paramref name="MainImageUrl"/> is display-only and plays no
/// part in checkout revalidation.
/// </summary>
public sealed record ProductCatalogSnapshot(
    Guid ProductId,
    string ProductName,
    decimal Price,
    int StockQuantity,
    string? MainImageUrl = null);
