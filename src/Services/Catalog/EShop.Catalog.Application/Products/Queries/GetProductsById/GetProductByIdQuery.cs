using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

/// <summary>
/// Query to get a product by ID with automatic distributed caching.
/// </summary>
public record GetProductByIdQuery : IRequest<Result<ProductDetailsDto>>, ICacheableQuery
{
    public Guid ProductId { get; init; }

    /// <summary>
    /// D1 / H5a. Set server-side by the endpoint from the caller's role — see
    /// <c>GetProductsQuery.IncludeUnpublished</c> for why it must never come from the client.
    /// An unpublished product is 404 without this and 200 with it.
    /// </summary>
    public bool IncludeUnpublished { get; init; }

    // Two variants, because the same product answers 200 to an admin and 404 to everyone else
    // while it is unpublished — one key would serve whichever answer was cached first to both.
    // Every product command evicts both via ProductCacheKeys.AllDetailVariants.
    public string CacheKey =>
        IncludeUnpublished
            ? ProductCacheKeys.DetailIncludingUnpublished(ProductId)
            : ProductCacheKeys.Detail(ProductId);
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}