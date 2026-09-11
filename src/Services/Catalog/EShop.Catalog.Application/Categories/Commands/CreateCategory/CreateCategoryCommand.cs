using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public record CreateCategoryCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string Name { get; init; } = string.Empty;

    public string? Slug { get; init; }

    public Guid? ParentCategoryId { get; init; }

    /// <summary>M11. Could only be set by a later PUT before Stage 8.</summary>
    public string? Description { get; init; }

    /// <summary>M11. Defaults to 0. Nullable so an omitted value binds rather than failing.</summary>
    public int? DisplayOrder { get; init; }

    /// <summary>
    /// M8. A new child appears in its parent's cached detail, so the parent's entry is evicted too —
    /// it used to be only the root list, leaving the parent stale for its full TTL.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => ParentCategoryId is { } parentId
        ? [CategoryCacheKeys.All, CategoryCacheKeys.Detail(parentId)]
        : [CategoryCacheKeys.All];
}
