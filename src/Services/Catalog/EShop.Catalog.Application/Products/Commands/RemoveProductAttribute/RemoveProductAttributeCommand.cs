using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.RemoveProductAttribute;

/// <summary>
/// Command to remove an attribute from a product (Admin panel S3). Attributes were add-only until
/// this existed — a mistyped one was permanent, and the 50-attribute cap was a one-way ratchet.
/// </summary>
public record RemoveProductAttributeCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public Guid AttributeId { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16 — see UpdateProductAttributeCommand for why the list family is bumped even though
    // today's list projection carries no attributes.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
