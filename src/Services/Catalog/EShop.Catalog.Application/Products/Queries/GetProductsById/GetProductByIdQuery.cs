using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

/// <summary>
/// Query to get a product by ID with automatic distributed caching.
/// </summary>
public record GetProductByIdQuery : IRequest<Result<ProductDto>>, ICacheableQuery
{
    public Guid ProductId { get; init; }

    public string CacheKey => $"product:{ProductId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}