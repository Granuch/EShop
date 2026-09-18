using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetLowStockProducts;

public class GetLowStockProductsQueryHandler
    : IRequestHandler<GetLowStockProductsQuery, Result<PagedResult<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public GetLowStockProductsQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(
        GetLowStockProductsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = new ProductListFilter(
            request.CategoryId,
            SearchTerm: null,
            MinPrice: null,
            MaxPrice: null,
            // Admin read: a draft that is out of stock is exactly what needs seeing before it is
            // published. The endpoint's Admin policy is what makes this safe.
            IncludeUnpublished: true,
            StockBelow: request.EffectiveThreshold);

        var (dtos, totalCount) = await _productQueryService.GetFilteredProductsAsync(
            filter,
            // Sorted by name rather than by stock: the query service's sort vocabulary is
            // Name/Price/CreatedAt, and adding a StockQuantity member to ProductSortBy for this one
            // read would widen the public list's contract too. The page is small and an admin scans
            // it by product, not by quantity.
            ProductSortBy.Name,
            isDescending: false,
            request.EffectivePageNumber,
            request.EffectivePageSize,
            cancellationToken);

        var pagedResult = PagedResult<ProductDto>.Create(
            dtos, request.EffectivePageNumber, request.EffectivePageSize, totalCount);

        return Result<PagedResult<ProductDto>>.Success(pagedResult);
    }
}
