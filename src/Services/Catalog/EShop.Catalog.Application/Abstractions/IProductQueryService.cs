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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets products by category as DTOs.
    /// </summary>
    Task<List<ProductDto>> GetProductsByCategoryAsync(
        Guid categoryId,
        int maxResults = 200,
        CancellationToken cancellationToken = default);
}
