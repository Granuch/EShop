using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public record UpdateCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
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
    /// Only the keys the command can name up front. The parent's and children's detail entries also
    /// embed this category (M8) but need the loaded entity, so the handler adds them through
    /// <c>ICacheInvalidationContext</c>.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [CategoryCacheKeys.Detail(Id), CategoryCacheKeys.All];
}
