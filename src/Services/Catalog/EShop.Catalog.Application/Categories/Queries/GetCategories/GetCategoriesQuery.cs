using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

/// <summary>
/// Query to get all root categories with children.
/// Implements ICacheableQuery for automatic distributed caching.
/// </summary>
public record GetCategoriesQuery : IRequest<Result<List<CategoryDto>>>, ICacheableQuery
{
    public string CacheKey => "categories:all";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
    public TimeSpan? SlidingExpiration => null;
}