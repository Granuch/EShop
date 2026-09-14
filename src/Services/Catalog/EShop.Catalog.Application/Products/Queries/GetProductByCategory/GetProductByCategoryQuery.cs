using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

/// <summary>
/// <c>GET /api/v1/categories/{id}/products</c>, paged the same way as <c>GET /api/v1/products</c>.
///
/// <para>
/// D5. This used to return a bare <c>List&lt;ProductDto&gt;</c> that stopped at 200 rows with no
/// total and no signal that anything was missing. The cap came from one sweeping refactor commit,
/// was asserted by no test and stated in no document, so it was an oversight rather than a contract.
/// </para>
///
/// <para>
/// <b>The cache key is in the versioned <c>products:list</c> family, and must stay there.</b> A
/// paged key embeds the page parameters, so no write can name every live page for exact-key
/// eviction — the DEBT-16 problem exactly. Before Stage 6 this key was a single
/// <c>products:category:{id}</c> that ten command handlers evicted by hand; those calls are gone,
/// because once pages exist they would evict a key nobody writes, which silently removes nothing
/// and logs success.
/// </para>
///
/// <para>
/// D1 / H5a. <b>Published products only, for every caller including admins.</b> Stage 4 chose
/// that because an admin variant would have been a second exact key nothing evicted. Being in the
/// family removes that constraint — a role variant would now invalidate correctly — so the
/// restriction is a decision rather than a limitation. Lifting it means adding
/// <c>IncludeUnpublished</c> here, overwriting it at the endpoint, and putting it in the key.
/// Admins meanwhile use <c>GET /api/v1/products?CategoryId=…</c>.
/// </para>
/// </summary>
public record GetProductByCategoryQuery : IRequest<Result<PagedResult<ProductDto>>>, ICacheableQuery, IVersionedCacheKey
{
    public Guid CategoryId { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;

    public string CacheKey => $"products:category:{CategoryId}:p={EffectivePageNumber}:ps={EffectivePageSize}";
    public string CacheKeyFamily => ProductCacheFamilies.ProductList;
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
