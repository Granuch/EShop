using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AddProductAttribute;

/// <summary>
/// Command to add a key/value attribute to an existing product. Returns the new attribute's ID.
/// Attributes are add-only — there is no update or remove command by design.
/// </summary>
public record AddProductAttributeCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

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
