using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryById;

/// <summary>
/// One category with its whole live subtree (F-37, frontend-contracts R5).
/// </summary>
/// <remarks>
/// Versioned in <see cref="CategoryCacheFamilies.CategoryList"/>, not evicted by exact key: the entry
/// embeds every descendant, so a write anywhere below makes it stale, and only a family bump can
/// reach every ancestor's entry. See <see cref="CategoryCacheKeys.Detail"/>.
/// </remarks>
public record GetCategoryByIdQuery : IRequest<Result<CategoryDto>>, ICacheableQuery, IVersionedCacheKey
{
    public Guid Id { get; init; }

    public string CacheKey => CategoryCacheKeys.Detail(Id);
    public string CacheKeyFamily => CategoryCacheFamilies.CategoryList;
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}