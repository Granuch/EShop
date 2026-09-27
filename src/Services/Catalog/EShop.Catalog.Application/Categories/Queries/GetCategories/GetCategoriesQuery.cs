using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

/// <summary>
/// Query to get all root categories with children.
/// Implements ICacheableQuery for automatic distributed caching.
/// </summary>
public record GetCategoriesQuery : IRequest<Result<List<CategoryDto>>>, ICacheableQuery, IVersionedCacheKey
{
    /// <summary>
    /// A4 (Admin panel S5). Whether deactivated categories are included. <b>Set server-side by the
    /// endpoint from the caller's role — never trust the bound value</b>, exactly as
    /// <c>GetProductsQuery.IncludeUnpublished</c> is.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>[AsParameters]</c> treats a non-nullable value type as a <i>required</i>
    /// query-string parameter, which would make every request that omits it fail binding with a 400
    /// reading "The request body is not valid JSON" — on a GET with no body.
    /// </remarks>
    public bool? IncludeInactive { get; init; }

    /// <summary>Absent means "public caller": active categories only.</summary>
    public bool EffectiveIncludeInactive => IncludeInactive ?? false;

    /// <summary>
    /// The flag is part of the key, and that is the load-bearing half of A4. Without it an admin's
    /// request populates the one shared entry with deactivated categories, and every anonymous
    /// caller reads them for the full ten-minute TTL — a cache-level leak the API itself would
    /// never allow.
    /// </summary>
    public string CacheKey => $"categories:list:inactive={EffectiveIncludeInactive}";

    /// <summary>
    /// DEBT-16 / A4. The key now has variants, so no write can name them all for exact-key
    /// eviction — the same problem paged product lists have. One family bump makes every variant
    /// unreachable. See <see cref="CategoryCacheFamilies.CategoryList"/> for why the old exact-key
    /// <c>categories:all</c> evictions were deleted rather than kept alongside this.
    /// </summary>
    public string CacheKeyFamily => CategoryCacheFamilies.CategoryList;

    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
    public TimeSpan? SlidingExpiration => null;
}
