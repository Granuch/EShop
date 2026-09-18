using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public record UpdateProductCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }

    /// <summary>
    /// New name, or <c>null</c> to leave it. Admin panel S2.
    /// </summary>
    /// <remarks>
    /// <b>Optional, and that is a deliberate compromise.</b> Full-replacement PUT semantics would
    /// make this required, but the endpoint has shipped for a long time accepting
    /// <c>{ productId, price, stockQuantity }</c> — requiring a name would 400 every existing
    /// caller, which is the "changing an existing contract" this repo avoids by default. So the
    /// four fields added here are all "omitted means leave alone", the same rule
    /// <c>UpdateProfileCommand</c> follows for <c>ProfilePictureUrl</c> (BUG-09). An admin form
    /// simply sends all of them.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// Three cases, not two (BUG-09): <c>null</c> leaves the stored description, blank clears it,
    /// anything else replaces it. <c>Product.UpdateDetails</c> enforces this.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// New SKU, or <c>null</c> to leave it. A SKU already held by another live product is refused
    /// with <c>Product.SkuConflict</c>; a race that slips past that pre-check is refused by the
    /// unique index and mapped to 409 by <c>AddProductSkuConflict()</c>.
    /// </summary>
    public string? Sku { get; init; }

    /// <summary>New category, or <c>null</c> to leave it. Refused with <c>Category.NotFound</c>.</summary>
    public Guid? CategoryId { get; init; }

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