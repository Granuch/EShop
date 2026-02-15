using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public record GetProductByCategoryQuery : IRequest<Result<List<ProductDto>>>, ICacheableQuery
{
    public Guid CategoryId { get; init; }

    public string CacheKey => $"products:category:{CategoryId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}