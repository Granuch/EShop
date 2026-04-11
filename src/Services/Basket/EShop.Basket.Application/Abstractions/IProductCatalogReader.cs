namespace EShop.Basket.Application.Abstractions;

public interface IProductCatalogReader
{
    Task<ProductCatalogSnapshot?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default);
}

public sealed record ProductCatalogSnapshot(Guid ProductId, string ProductName, decimal Price);
