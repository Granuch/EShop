using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.PublishProduct;

/// <summary>
/// Makes a draft product publicly visible (D1 / H5a).
///
/// <para>
/// Publishing changes which products appear in every public read path, so it must invalidate the
/// same keys a price or stock change does — the product's own detail entry, its category list, and
/// the whole <c>products:list</c> family.
/// </para>
/// </summary>
public record PublishProductCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
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
