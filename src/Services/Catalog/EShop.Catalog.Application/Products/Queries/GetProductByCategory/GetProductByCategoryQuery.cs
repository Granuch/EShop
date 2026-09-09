using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public record GetProductByCategoryQuery : IRequest<Result<List<ProductDto>>>, ICacheableQuery
{
    public Guid CategoryId { get; init; }

    /// <summary>
    /// D1 / H5a. **This endpoint always returns published products only**, for every caller
    /// including admins — unlike <c>GetProductsQuery</c>, which honours the caller's role.
    ///
    /// <para>
    /// The asymmetry is deliberate. Varying this response by role means varying its cache key by
    /// role, and <c>products:category:{id}</c> is invalidated <b>by exact key</b> from every
    /// product write — so an admin-only variant would be a second key that nothing evicts and that
    /// would serve stale drafts for its full TTL. Admins manage drafts through
    /// <c>GET /api/v1/products?CategoryId=…</c>, which is in the versioned <c>products:list</c>
    /// family and therefore invalidates correctly for both variants.
    /// </para>
    /// </summary>
    public string CacheKey => $"products:category:{CategoryId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}