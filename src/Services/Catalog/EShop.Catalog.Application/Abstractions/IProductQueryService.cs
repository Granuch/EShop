using EShop.Catalog.Application.Products.Queries.GetNewestProducts;
using EShop.Catalog.Application.Products.Queries.GetProducts;

namespace EShop.Catalog.Application.Abstractions;

/// <summary>
/// Query service for product read operations with filtering, sorting, and pagination.
/// Implemented in Infrastructure to keep provider-specific query logic (EF.Functions, ILIKE, etc.)
/// out of the Application layer.
/// </summary>
public interface IProductQueryService
{
    /// <summary>
    /// Gets one OFFSET page of product DTOs, plus the total row count under the same filter.
    /// </summary>
    Task<(List<ProductDto> Items, int TotalCount)> GetFilteredProductsAsync(
        ProductListFilter filter,
        ProductSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets up to <paramref name="take"/> products in newest-first keyset order
    /// (<c>CreatedAt DESC, Id DESC</c>), strictly after <paramref name="after"/> when given.
    /// No count is taken — avoiding it is half the point of keyset paging.
    /// </summary>
    /// <summary>
    /// The counts behind <c>GET /api/v1/categories/{id}/stats</c> (Admin panel S5), taken in one
    /// round trip rather than as five separate queries.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in <c>ICategoryRepository</c> because it counts <b>products</b>, and
    /// the repository is the Category aggregate's write-side boundary; this is a read projection,
    /// which is what this service is for.
    /// </remarks>
    Task<CategoryProductStats> GetCategoryProductStatsAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);

    Task<List<ProductDto>> GetNewestProductsAsync(
        ProductListFilter filter,
        ProductCursor? after,
        int take,
        CancellationToken cancellationToken = default);
}
