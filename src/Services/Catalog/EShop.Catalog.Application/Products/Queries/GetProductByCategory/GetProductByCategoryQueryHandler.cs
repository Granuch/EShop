using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public sealed class GetProductByCategoryQueryHandler : IRequestHandler<GetProductByCategoryQuery, Result<PagedResult<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public GetProductByCategoryQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductByCategoryQuery request, CancellationToken cancellationToken)
    {
        // The same query GET /products runs for ?CategoryId=, rather than a second "products by
        // category" implementation that can drift from it. Published only — see the query's doc.
        var filter = new ProductListFilter(
            request.CategoryId,
            SearchTerm: null,
            MinPrice: null,
            MaxPrice: null,
            IncludeUnpublished: false);

        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        var (dtos, totalCount) = await _productQueryService.GetFilteredProductsAsync(
            filter,
            ProductSortBy.Name,
            isDescending: false,
            pageNumber,
            pageSize,
            cancellationToken);

        return Result<PagedResult<ProductDto>>.Success(
            PagedResult<ProductDto>.Create(dtos, pageNumber, pageSize, totalCount));
    }
}
