using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Abstractions;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Handler for getting products with filtering and pagination.
/// Delegates query composition to IProductQueryService (Infrastructure).
/// Caching is handled by CachingBehavior via ICacheableQuery.
/// </summary>
public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, Result<PagedResult<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public GetProductsQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        var (dtos, totalCount) = await _productQueryService.GetFilteredProductsAsync(
            request.CategoryId,
            request.SearchTerm,
            request.MinPrice,
            request.MaxPrice,
            request.EffectiveSortBy,
            request.EffectiveIsDescending,
            pageNumber,
            pageSize,
            request.Cursor,
            cancellationToken);

        var pagedResult = PagedResult<ProductDto>.Create(dtos, pageNumber, pageSize, totalCount);

        return Result<PagedResult<ProductDto>>.Success(pagedResult);
    }
}
