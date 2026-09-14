using System.Collections.Concurrent;
using EShop.Basket.Application.Abstractions;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for Catalog. A product must be added here before a basket can hold it, and its price and stock here are
/// the only ones Basket can get — the same role Ordering's fixture of the same name plays. A removed product reads as
/// the public catalog shows a deleted, unpublished or discontinued one: not found.
/// </summary>
public sealed class FakeProductCatalog : IProductCatalogReader
{
    private readonly ConcurrentDictionary<Guid, ProductCatalogSnapshot> _products = new();

    /// <summary>When set, every lookup fails the way an unreachable Catalog does.</summary>
    public bool IsUnavailable { get; set; }

    public Guid Add(string name, decimal price, int stock = 100)
    {
        var id = Guid.NewGuid();
        _products[id] = new ProductCatalogSnapshot(id, name, price, stock);
        return id;
    }

    /// <summary>Changes what Catalog reports for an existing product, as a later edit in Catalog would.</summary>
    public void Update(Guid id, string name, decimal price)
        => _products[id] = _products[id] with { ProductName = name, Price = price };

    public void SetPrice(Guid id, decimal price) => _products[id] = _products[id] with { Price = price };

    public void SetStock(Guid id, int stock) => _products[id] = _products[id] with { StockQuantity = stock };

    /// <summary>Takes the product out of the public catalog.</summary>
    public void Remove(Guid id) => _products.TryRemove(id, out _);

    public Task<ProductCatalogSnapshot?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (IsUnavailable)
        {
            throw new HttpRequestException("Catalog is down (test double).");
        }

        return Task.FromResult(_products.TryGetValue(productId, out var product) ? product : null);
    }
}
