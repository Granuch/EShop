using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AddProductImage;

/// <summary>
/// Command to add an image to an existing product. Returns the new image's ID.
/// </summary>
public record AddProductImageCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. products:list:* keys embed every filter/sort/page parameter and cannot be named,
    // so the family version is bumped instead. The handler still adds products:category:{id} to
    // ICacheInvalidationContext, because the command does not know the CategoryId.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
