using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetLowStockProducts;

/// <summary>
/// Admin-only "what is running out" read (Admin panel S4), backing the dashboard widget.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>?StockBelow=</c> with an opinionated default and a fixed sort, not a second query
/// path: it goes through the same <c>ProductListFilter</c> and
/// <c>ProductQueryService.GetFilteredProductsAsync</c>, so it cannot drift from the main list's
/// projection or its visibility rules. The endpoint exists because a dashboard widget wants one
/// stable URL rather than a filter combination it has to remember.
/// </para>
/// <para>
/// <b>Includes unpublished products</b>, unlike the public list: a draft product that is out of
/// stock is exactly what an admin needs to see before publishing it. Not cached, for the same
/// reason as the deleted list — the audience is one admin screen and stale stock is worse than a
/// query.
/// </para>
/// </remarks>
public record GetLowStockProductsQuery : IRequest<Result<PagedResult<ProductDto>>>
{
    /// <summary>Strictly below this. Defaults to 10; <c>threshold=1</c> is "out of stock".</summary>
    public int? Threshold { get; init; }

    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public Guid? CategoryId { get; init; }

    public int EffectiveThreshold => Threshold ?? 10;
    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
}
