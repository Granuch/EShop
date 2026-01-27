using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Query to get paginated list of products
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
