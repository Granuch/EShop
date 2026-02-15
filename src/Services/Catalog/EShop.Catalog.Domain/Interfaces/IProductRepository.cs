using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Domain.Interfaces;

/// <summary>
/// Repository interface for Product aggregate
/// </summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<IEnumerable<Product?>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(Product product, CancellationToken cancellationToken = default);
    IQueryable<Product> Query();
}
