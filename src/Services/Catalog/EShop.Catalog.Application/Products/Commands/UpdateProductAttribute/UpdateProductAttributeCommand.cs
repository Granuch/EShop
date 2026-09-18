using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProductAttribute;

/// <summary>
/// Command to replace an existing attribute's name and value (Admin panel S3). Attributes could
/// only be added before this, so fixing a typo in "Colour" meant adding a second attribute and
/// having no way to delete the first.
/// </summary>
/// <remarks>
/// Both fields are required — full-replacement PUT semantics, as with
/// <c>UpdateProductImageCommand</c> and for the same reason: this endpoint is new and has no
/// existing caller sending a partial body to protect.
/// </remarks>
public record UpdateProductAttributeCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public Guid AttributeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. The list projection does not carry attributes today, but the family is bumped anyway
    // — the alternative is a cache contract that depends on the current shape of a DTO, which is
    // exactly the coupling that left ten handlers naming a stale key in Stage 6.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
