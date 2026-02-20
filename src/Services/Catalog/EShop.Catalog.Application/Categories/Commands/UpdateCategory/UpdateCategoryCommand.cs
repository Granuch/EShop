using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public record UpdateCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate => [$"category:{Id}", "categories:all"];
}