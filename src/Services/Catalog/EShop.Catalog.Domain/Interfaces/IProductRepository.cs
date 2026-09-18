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
    /// Loads a product for tracking <b>including soft-deleted ones</b>, via
    /// <c>IgnoreQueryFilters()</c> (Admin panel S4).
    /// </summary>
    /// <remarks>
    /// Exists only for the restore path, which by definition cannot find its target through
    /// <see cref="GetByIdAsync"/> — the <c>!p.IsDeleted</c> global filter hides it, so that method
    /// answers null and the handler would report 404 for a product that is plainly there. Do not
    /// reach for this anywhere else: every other write path must keep treating a deleted product as
    /// absent, which is what the filter is for.
    /// </remarks>
    Task<Product?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Whether a <b>live</b> product already holds this SKU. Runs under the <c>!p.IsDeleted</c>
    /// global query filter, deliberately matching the partial unique index
    /// <c>IX_Products_Sku ... WHERE NOT "IsDeleted"</c> — so a soft-deleted product's SKU reads as
    /// free here and the database agrees.
    /// </summary>
    /// <param name="excludingProductId">
    /// A product to ignore, so an update that keeps its own SKU does not collide with itself.
    /// Omitted on the create path, where there is no such product.
    /// </param>
    Task<bool> SkuExistsAsync(string sku, Guid? excludingProductId = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Whether any <b>live</b> product is in this category. Runs under the <c>!p.IsDeleted</c>
    /// global query filter, so soft-deleted products do not count — and since Product → Category
    /// is <c>ON DELETE CASCADE</c>, deleting such a category removes its soft-deleted products with
    /// it. That is unchanged from the list-loading form this replaced; category deletion semantics
    /// belong to audit item M12.
    /// </summary>
    Task<bool> AnyInCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(Product product, CancellationToken cancellationToken = default);
}
