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
    /// Gets a filtered, sorted, and paginated list of product DTOs.
    /// </summary>
    /// <param name="includeUnpublished">
    /// D1 / H5a. False for public callers, which restricts the result to
    /// <c>ProductStatus.Active</c>. Set from the caller's role at the endpoint — never from a bound
    /// request property, or a client could ask for the unpublished catalog.
    /// </param>
    Task<(List<ProductDto> Items, int TotalCount)> GetFilteredProductsAsync(
        Guid? categoryId,
        string? searchTerm,
        decimal? minPrice,
        decimal? maxPrice,
        ProductSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        DateTime? cursor = null,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets products by category as DTOs.
    /// </summary>
    Task<List<ProductDto>> GetProductsByCategoryAsync(
        Guid categoryId,
        int maxResults = 200,
        CancellationToken cancellationToken = default);
}
