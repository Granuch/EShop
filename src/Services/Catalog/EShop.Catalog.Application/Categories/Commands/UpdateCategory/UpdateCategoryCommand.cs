using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public record UpdateCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Category";

    string? IAuditedCommand.AuditEntityId => Id.ToString();

    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// M10. Omitted (<c>null</c>) leaves the stored description, blank clears it, anything else
    /// replaces it. This was a non-nullable <c>string</c> defaulting to <c>""</c>, so a PUT carrying
    /// only a name wiped the description — the BUG-09 shape. Keep it nullable.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>Omitted leaves the stored display order.</summary>
    public int? DisplayOrder { get; init; }

    /// <summary>
    /// F-39 (frontend-contracts R5). Omitted (<c>null</c>) leaves the stored slug; anything sent
    /// must be a valid slug, free among the category's live siblings (409 <c>Category.SlugConflict</c>
    /// otherwise). Before this no endpoint could change a slug at all.
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// Empty on purpose. A rename shows in the category's own detail, its children's
    /// (<c>parentCategoryName</c>) and every ancestor's (F-37: the detail carries the whole subtree),
    /// and both category reads are versioned in <see cref="CategoryCacheFamilies.CategoryList"/>, so
    /// the family bump below evicts all of them. An exact key would match nothing and log success.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [];

    public IEnumerable<string> CacheFamiliesToInvalidate => [CategoryCacheFamilies.CategoryList];
}
