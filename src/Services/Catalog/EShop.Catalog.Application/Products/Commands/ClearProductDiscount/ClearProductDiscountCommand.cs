using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ClearProductDiscount;

/// <summary>
/// Ends a promotion, returning the customer-facing price to the product's list price (H5b / D3).
///
/// <para>
/// A separate command rather than <c>SetProductDiscount</c> with a nullable price: an optional
/// scalar that means "clear it" when null and "leave it" when omitted is the exact ambiguity the
/// repo already carries as BUG-09, and JSON gives no way to tell the two apart without extra
/// machinery. Two verbs, no ambiguity.
/// </para>
/// </summary>
public record ClearProductDiscountCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. products:list:* keys embed every filter/sort/page parameter and cannot be named,
    // so the family version is bumped instead. That one bump also covers the per-category product
    // pages (GET /categories/{id}/products), which joined this family in Stage 6.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
