using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryById;

public record GetCategoryByIdQuery : IRequest<Result<CategoryDto>>, ICacheableQuery
{
    public Guid Id { get; init; }

    public string CacheKey => $"category:{Id}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}