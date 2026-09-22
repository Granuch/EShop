using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ReorderProductImages;

/// <summary>
/// Command to set gallery order from an ordered list of image ids (Admin panel S3).
/// <c>DisplayOrder</c> was assignable only at add time, so reordering a gallery meant deleting and
/// re-adding every image.
/// </summary>
/// <remarks>
/// <c>ImageIds</c> must list every image of the product exactly once — see
/// <c>Product.ReorderImages</c> for why a partial list is refused rather than appended to. It is
/// nullable because System.Text.Json writes an absent optional collection as explicit
/// <c>null</c>, which would overwrite a <c>= []</c> initializer; the validator turns that into a
/// readable 400 instead of a NullReferenceException.
/// </remarks>
public record ReorderProductImagesCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Product";

    string? IAuditedCommand.AuditEntityId => ProductId.ToString();

    public Guid ProductId { get; init; }
    public IReadOnlyList<Guid>? ImageIds { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. Reordering does not change which image is main, so the list projection's image URL
    // is unaffected — but the list is bumped anyway rather than reasoned about, because that
    // reasoning is a property of today's projection and would silently become wrong the moment
    // the list carried a second image or ordered by DisplayOrder.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
