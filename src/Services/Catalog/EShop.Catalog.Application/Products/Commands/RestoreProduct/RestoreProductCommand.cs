using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.RestoreProduct;

/// <summary>
/// Command to bring a soft-deleted product back (Admin panel S4). <c>DELETE /products/{id}</c> has
/// always been a soft delete, but nothing could undo it — the row stayed invisible behind the
/// <c>!IsDeleted</c> global query filter forever.
/// </summary>
/// <remarks>
/// The product comes back as <c>Draft</c>, never <c>Active</c>: see <c>Product.Restore</c>.
/// </remarks>
public record RestoreProductCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. A restored product joins every unfiltered list page, so the family must be bumped.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
