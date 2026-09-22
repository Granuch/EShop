using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public record CreateCategoryCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Category";

    string? IAuditedCommand.AuditEntityId => null;

    string? IAuditedCommand.AuditEntityIdFromResult(object? value) => value is Guid id ? id.ToString() : null;

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
    /// <remarks>
    /// A4 (Admin panel S5): <c>CategoryCacheKeys.All</c> was removed from this list. The tree read's
    /// key now embeds <c>includeInactive</c>, so naming one fixed string would evict at most one of
    /// its variants — and since S5 nothing writes the old <c>categories:all</c> at all, so the call
    /// would remove nothing while logging success. The family bump below replaces it.
    /// </remarks>
    public IEnumerable<string> CacheKeysToInvalidate => ParentCategoryId is { } parentId
        ? [CategoryCacheKeys.Detail(parentId)]
        : [];

    public IEnumerable<string> CacheFamiliesToInvalidate => [CategoryCacheFamilies.CategoryList];
}
