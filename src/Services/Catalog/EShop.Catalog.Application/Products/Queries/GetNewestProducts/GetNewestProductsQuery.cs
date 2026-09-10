using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetNewestProducts;

/// <summary>
/// H4. <c>GET /api/v1/products/newest</c> — products newest first, paged by keyset cursor.
///
/// <para>
/// This replaces <c>GetProductsQuery.Cursor</c>, which was honoured only for
/// <c>SortBy=CreatedAt&amp;IsDescending=true</c>, silently ignored for every other combination
/// (serving offset page 1 with a 200), wrapped in an offset-shaped <c>PagedResult</c> whose
/// <c>hasNextPage</c> stayed true on the last page, and never told the client what to send next.
/// A separate endpoint with one fixed order means there is no sort combination to get wrong, and a
/// response type that is cursor-shaped from the first page.
/// </para>
///
/// <para>
/// <b>Forward-only.</b> <c>nextCursor</c> is issued; <c>previousCursor</c> is not, so
/// <c>hasPreviousPage</c> is always false here — a client paging back keeps the cursors it has
/// already used. There is no total count either: counting is the cost keyset paging exists to avoid.
/// </para>
///
/// <para>
/// Every value type is nullable for the <c>[AsParameters]</c> reason documented on
/// <c>GetProductsQuery</c>: a non-nullable one would be a required query-string parameter.
/// </para>
/// </summary>
public record GetNewestProductsQuery : IRequest<Result<CursorPagedResult<ProductDto>>>, ICacheableQuery, IVersionedCacheKey
{
    /// <summary>The <c>nextCursor</c> from the previous page, unchanged. Omit for the first page.</summary>
    public string? Cursor { get; init; }
    public int? PageSize { get; init; }
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// D1 / H5a. <b>Overwritten server-side by the endpoint from the caller's role</b> — see
    /// <c>GetProductsQuery.IncludeUnpublished</c>. Bound like every other property here, so without
    /// that overwrite <c>?IncludeUnpublished=true</c> would expose drafts.
    /// </summary>
    public bool? IncludeUnpublished { get; init; }

    public bool EffectiveIncludeUnpublished => IncludeUnpublished ?? false;
    public int EffectivePageSize => PageSize ?? 10;

    // IncludeUnpublished is in the key for the same reason as on GetProductsQuery: without it an
    // admin's page, drafts included, would be cached and served to anonymous callers.
    public string CacheKey =>
        $"products:newest:cat={CategoryId}:s={SearchTerm}:min={MinPrice}:max={MaxPrice}" +
        $":ps={EffectivePageSize}:cur={Cursor}:unpub={EffectiveIncludeUnpublished}";

    // Same family as every other product list, so any product write invalidates every cached page.
    public string CacheKeyFamily => ProductCacheFamilies.ProductList;

    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
