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
/// 
/// Non-nullable value types use nullable wrappers so that [AsParameters] binding
/// treats them as optional query string parameters. Defaults are applied in the handler.
/// </summary>
public record GetProductsQuery : IRequest<Result<PagedResult<ProductDto>>>
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public ProductSortBy? SortBy { get; init; }
    public bool? IsDescending { get; init; }

    // Convenience accessors with defaults applied
    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
    public ProductSortBy EffectiveSortBy => SortBy ?? ProductSortBy.Name;
    public bool EffectiveIsDescending => IsDescending ?? false;
}

public enum ProductSortBy
{
    Name,
    Price,
    CreatedAt
}
