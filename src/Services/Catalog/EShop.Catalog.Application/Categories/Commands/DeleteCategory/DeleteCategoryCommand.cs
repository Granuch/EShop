using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.DeleteCategory;

public record DeleteCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid Id { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => [$"category:{Id}", "categories:all"];
}