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
/// D1 / H5a. Admins see drafts, everyone else published products only — see
/// <see cref="IncludeUnpublished"/> (Admin panel S5, #54). Before S5 it was published-only for every
/// caller, because an admin variant would have been a second exact key nothing evicted; the family
/// removed that constraint.
/// </para>
///
/// <para>
/// The category means its <b>whole subtree</b>: products filed under any descendant are included, at
/// any depth. The resolution happens once, in <c>ProductQueryService</c>, and applies identically to
/// <c>GET /api/v1/products?CategoryId=…</c>. The key needs nothing for it — the id does not change,
/// only its meaning — and a move or restore that reshapes a subtree already bumps the family.
/// </para>
/// </summary>
public record GetProductByCategoryQuery : IRequest<Result<PagedResult<ProductDto>>>, ICacheableQuery, IVersionedCacheKey
{
    public Guid CategoryId { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    /// <summary>
    /// D1 / H5a (Admin panel S5, endpoint #54). Whether unpublished products are included.
    /// <b>Set server-side by the endpoint from the caller's role — never trust the bound value</b>,
    /// exactly as <c>GetProductsQuery.IncludeUnpublished</c> is, and for the same reason: this
    /// record binds from the route and query string, so a client could otherwise ask for it.
    /// </summary>
    /// <remarks>
    /// Nullable so <c>[AsParameters]</c> keeps it optional; a non-nullable value type there is a
    /// required query-string parameter.
    /// </remarks>
    public bool? IncludeUnpublished { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
    public bool EffectiveIncludeUnpublished => IncludeUnpublished ?? false;

    /// <summary>
    /// The visibility flag is part of the key and must stay that way: an admin's page contains
    /// draft products, and without it in the key that response would be cached and then served to
    /// anonymous callers — leaking the unpublished catalogue through the cache rather than the API.
    /// Safe to vary only because this key is in the versioned <c>products:list</c> family, so both
    /// variants are evicted by one bump; that is what the Stage 4 note above meant by the
    /// restriction being a decision rather than a limitation.
    /// </summary>
    public string CacheKey =>
        $"products:category:{CategoryId}:p={EffectivePageNumber}:ps={EffectivePageSize}" +
        $":unpub={EffectiveIncludeUnpublished}";
    public string CacheKeyFamily => ProductCacheFamilies.ProductList;
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
