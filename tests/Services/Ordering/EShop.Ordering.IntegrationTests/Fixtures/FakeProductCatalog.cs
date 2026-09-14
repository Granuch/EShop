using System.Collections.Concurrent;
using EShop.Ordering.Domain.Interfaces;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for Catalog. A product must be added here before an order can reference it, and its
/// price here is the only price an order line can get — which is what lets a test prove a
/// client-supplied price is ignored.
/// </summary>
public sealed class FakeProductCatalog : IProductCatalogReader
{
    private readonly ConcurrentDictionary<Guid, CatalogProduct> _products = new();

    /// <summary>When set, every lookup fails as if Catalog were down.</summary>
    public bool IsUnavailable { get; set; }

    public Guid Add(string name, decimal unitPrice)
    {
        var id = Guid.NewGuid();
        _products[id] = new CatalogProduct(id, name, unitPrice);
        return id;
    }

    public Task<CatalogProduct?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (IsUnavailable)
        {
            throw new CatalogUnavailableException("Catalog is down (test double).");
        }

        return Task.FromResult(_products.TryGetValue(productId, out var product) ? product : null);
    }
}
