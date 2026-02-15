using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Query to get paginated list of products.
/// 
/// NOT cached: The combination of filters, search terms, pagination, and sorting
/// produces too many key permutations for effective cache invalidation via
/// IDistributedCache (no pattern-based removal support). Stale list data after
/// product mutations is worse than a DB round-trip.
/// 
/// Single-product and by-category queries ARE cached because they have
/// deterministic, invalidatable keys.
/// </summary>
public record GetProductsQuery : IRequest<Result<PagedResult<ProductDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public ProductSortBy SortBy { get; init; } = ProductSortBy.Name;
    public bool IsDescending { get; init; } = false;
}

public enum ProductSortBy
{
    Name,
    Price,
    CreatedAt
}
