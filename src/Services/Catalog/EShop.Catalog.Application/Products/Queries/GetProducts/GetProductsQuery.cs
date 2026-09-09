using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Query to get paginated list of products.
/// 
/// Cached with a composite key that includes every filter/sort/pagination parameter
/// so each unique parameter combination gets its own cache entry.
/// Cache TTL is 5 minutes (absolute) — acceptable staleness for product listings.
/// 
/// Single-product and by-category queries are also cached with deterministic keys.
/// 
/// Non-nullable value types use nullable wrappers so that [AsParameters] binding
/// treats them as optional query string parameters. Defaults are applied in the handler.
/// </summary>
public record GetProductsQuery : IRequest<Result<PagedResult<ProductDto>>>, ICacheableQuery, IVersionedCacheKey
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public ProductSortBy? SortBy { get; init; }
    public bool? IsDescending { get; init; }

    /// <summary>
    /// Optional cursor for keyset pagination (CreatedAt value of the last item on the previous page).
    /// When provided, uses cursor-based pagination instead of OFFSET — constant performance regardless of page depth.
    /// </summary>
    public DateTime? Cursor { get; init; }

    /// <summary>
    /// D1 / H5a. Whether unpublished (Draft) products are included. **Set server-side by the
    /// endpoint from the caller's role — never trust the bound value.**
    ///
    /// <para>
    /// This record is bound with <c>[AsParameters]</c>, so every public property is a query-string
    /// parameter and a client could otherwise simply pass <c>?IncludeUnpublished=true</c> to read
    /// the unpublished catalog. <c>ProductEndpoints</c> overwrites it with
    /// <c>query with { IncludeUnpublished = user.IsInRole("Admin") }</c> after binding, which is
    /// what makes that impossible. Any new read path must do the same.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>Nullable on purpose, like every other property here.</b> `[AsParameters]` treats a
    /// non-nullable value type as a <i>required</i> query-string parameter, so declaring this
    /// `bool` made every request that omitted it fail binding — and the resulting 400 says
    /// "The request body is not valid JSON", which is doubly misleading on a GET with no body.
    /// </remarks>
    public bool? IncludeUnpublished { get; init; }

    // Convenience accessors with defaults applied
    /// <summary>Absent means "public caller": published products only.</summary>
    public bool EffectiveIncludeUnpublished => IncludeUnpublished ?? false;
    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
    public ProductSortBy EffectiveSortBy => SortBy ?? ProductSortBy.Name;
    public bool EffectiveIsDescending => IsDescending ?? false;

    // ICacheableQuery implementation
    // IncludeUnpublished is part of the key and must stay that way: an admin's list contains draft
    // products, and without it in the key that response would be cached and then served to
    // anonymous callers — leaking the unpublished catalog through the cache rather than the API.
    public string CacheKey =>
        $"products:list:cat={CategoryId}:s={SearchTerm}:min={MinPrice}:max={MaxPrice}" +
        $":sort={EffectiveSortBy}:desc={EffectiveIsDescending}:p={EffectivePageNumber}:ps={EffectivePageSize}" +
        $":cur={Cursor?.Ticks}:unpub={EffectiveIncludeUnpublished}";

    /// <summary>
    /// DEBT-16. The key above embeds ten filter/sort/page parameters, so the set of live keys is
    /// unbounded and no write can name them for exact-key invalidation — list results used to stay
    /// stale for the full 5-minute TTL after any product change, a fact that had been copy-pasted
    /// as a comment into four command handlers instead of being fixed.
    ///
    /// <para>
    /// Declaring the family makes <c>CachingBehavior</c> fold the family's current version into
    /// every key it writes, so a product write bumps one counter and the whole family becomes
    /// unreachable at once. Note the version is resolved per request, so this costs one extra
    /// cache read on the list path.
    /// </para>
    /// </summary>
    public string CacheKeyFamily => ProductCacheFamilies.ProductList;

    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}

public enum ProductSortBy
{
    Name,
    Price,
    CreatedAt
}
