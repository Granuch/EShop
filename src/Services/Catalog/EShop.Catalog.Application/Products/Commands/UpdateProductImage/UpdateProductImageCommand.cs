using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProductImage;

/// <summary>
/// Command to replace an existing image's URL and alt text (Admin panel S3). Until this existed,
/// correcting a mistyped CDN link meant deleting the image and adding it again, which loses its
/// position in the gallery and its main flag.
/// </summary>
/// <remarks>
/// <b>Full-replacement semantics, unlike <c>UpdateProductCommand</c>.</b> <c>Url</c> is required and
/// <c>AltText</c> is replaced outright — omitting it clears it. The three-case BUG-09 contract that
/// <c>UpdateProductCommand</c> follows exists to protect an endpoint that already shipped accepting
/// a partial body; this endpoint is new, so it has no such caller to protect and can use the
/// simpler, unambiguous PUT semantics instead.
/// </remarks>
public record UpdateProductImageCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public Guid ImageId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. products:list:* keys embed every filter/sort/page parameter and cannot be named,
    // so the family version is bumped instead. The list carries the main image's URL, so an edit
    // to it is visible there and this bump is not optional.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
