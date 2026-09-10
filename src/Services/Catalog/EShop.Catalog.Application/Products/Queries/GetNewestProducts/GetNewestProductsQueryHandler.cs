using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetNewestProducts;

public sealed class GetNewestProductsQueryHandler
    : IRequestHandler<GetNewestProductsQuery, Result<CursorPagedResult<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public GetNewestProductsQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<CursorPagedResult<ProductDto>>> Handle(
        GetNewestProductsQuery request,
        CancellationToken cancellationToken)
    {
        // GetNewestProductsQueryValidator has already rejected an unreadable cursor; Decode throws
        // if that ever stops being true rather than quietly serving page one.
        ProductCursor? after = string.IsNullOrEmpty(request.Cursor)
            ? null
            : ProductCursor.Decode(request.Cursor);

        var pageSize = request.EffectivePageSize;
        var filter = new ProductListFilter(
            request.CategoryId,
            request.SearchTerm,
            request.MinPrice,
            request.MaxPrice,
            request.EffectiveIncludeUnpublished);

        // One row more than the page. Its presence is the only honest answer to "is there a next
        // page": issuing a cursor whenever the page is full would hand out a cursor to an empty page
        // whenever the total is an exact multiple of the page size.
        var rows = await _productQueryService.GetNewestProductsAsync(filter, after, pageSize + 1, cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.GetRange(0, pageSize) : rows;
        var nextCursor = hasMore
            ? new ProductCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return Result<CursorPagedResult<ProductDto>>.Success(
            CursorPagedResult<ProductDto>.Create(items, pageSize, nextCursor));
    }
}
