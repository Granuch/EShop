using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetDeletedProducts;

public class GetDeletedProductsQueryHandler
    : IRequestHandler<GetDeletedProductsQuery, Result<PagedResult<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public GetDeletedProductsQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(
        GetDeletedProductsQuery request,
        CancellationToken cancellationToken)
    {
        // IncludeUnpublished must be true here, and it is not a privilege decision: SoftDelete sets
        // Status to Discontinued, so every deleted product fails the published-only predicate and
        // passing false would make this endpoint return an empty page forever. The endpoint's Admin
        // policy is what restricts who reaches this handler at all.
        var filter = new ProductListFilter(
            request.CategoryId,
            request.SearchTerm,
            MinPrice: null,
            MaxPrice: null,
            IncludeUnpublished: true,
            DeletedOnly: true);

        var (dtos, totalCount) = await _productQueryService.GetFilteredProductsAsync(
            filter,
            // Newest deletions first is the useful order for a recycle bin: what an admin wants to
            // undo is almost always what they just deleted.
            ProductSortBy.CreatedAt,
            isDescending: true,
            request.EffectivePageNumber,
            request.EffectivePageSize,
            cancellationToken);

        var pagedResult = PagedResult<ProductDto>.Create(
            dtos, request.EffectivePageNumber, request.EffectivePageSize, totalCount);

        return Result<PagedResult<ProductDto>>.Success(pagedResult);
    }
}
