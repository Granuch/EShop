using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Domain.Interfaces;

/// <summary>
/// Repository interface for Product aggregate
/// </summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Whether a <b>live</b> product already holds this SKU. Runs under the <c>!p.IsDeleted</c>
    /// global query filter, deliberately matching the partial unique index
    /// <c>IX_Products_Sku ... WHERE NOT "IsDeleted"</c> — so a soft-deleted product's SKU reads as
    /// free here and the database agrees.
    /// </summary>
    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default);
    Task<IEnumerable<Product>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(Product product, CancellationToken cancellationToken = default);
    IQueryable<Product> Query();
}
