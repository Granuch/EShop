using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.ReorderCategories;

/// <summary>
/// Command to set sibling order from an ordered list of ids (Admin panel S5, endpoint #51).
/// <c>DisplayOrder</c> was settable only through a full <c>PUT</c> per category, so reordering a
/// level meant one request per sibling with no atomicity — a half-applied reorder left duplicate
/// positions.
/// </summary>
/// <remarks>
/// <para>
/// The order applies <b>within one parent</b>. <c>ParentCategoryId</c> is null for the roots, and
/// like <c>MoveCategoryCommand</c> that makes "omitted" and "explicitly null" the same on the wire
/// — deliberate here, because reordering roots is the common case and a dedicated request body
/// makes the intent explicit.
/// </para>
/// <para>
/// <c>CategoryIds</c> must list every live sibling exactly once, for the reason
/// <c>Product.ReorderImages</c> documents: a partial reorder has no correct answer for where the
/// omitted ones land.
/// </para>
/// </remarks>
public record ReorderCategoriesCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid? ParentCategoryId { get; init; }
    public IReadOnlyList<Guid>? CategoryIds { get; init; }

    /// <summary>
    /// The parent's detail lists its children in order, so it goes stale. The reordered categories'
    /// own detail entries do not carry their position, but are evicted anyway by the handler
    /// through <c>ICacheInvalidationContext</c> rather than reasoned about here — that reasoning is
    /// a property of today's DTO.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => ParentCategoryId is { } parentId
        ? [CategoryCacheKeys.Detail(parentId)]
        : [];

    public IEnumerable<string> CacheFamiliesToInvalidate => [CategoryCacheFamilies.CategoryList];
}
