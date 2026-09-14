using System.Collections.Concurrent;
using EShop.Basket.Application.Abstractions;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for Catalog. A product must be added here before a basket can hold it, and its price here is the
/// only price a basket line can get — the same role Ordering's fixture of the same name plays.
/// </summary>
public sealed class FakeProductCatalog : IProductCatalogReader
{
    private readonly ConcurrentDictionary<Guid, ProductCatalogSnapshot> _products = new();

    /// <summary>When set, every lookup fails the way an unreachable Catalog does.</summary>
    public bool IsUnavailable { get; set; }

    public Guid Add(string name, decimal price)
    {
        var id = Guid.NewGuid();
        _products[id] = new ProductCatalogSnapshot(id, name, price);
        return id;
    }

    public Task<ProductCatalogSnapshot?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (IsUnavailable)
        {
            throw new HttpRequestException("Catalog is down (test double).");
        }

        return Task.FromResult(_products.TryGetValue(productId, out var product) ? product : null);
    }
}
