using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public record CreateCategoryCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string Name { get; init; } = string.Empty;

    public string? Slug { get; init; }

    public Guid? ParentCategoryId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => ["categories:all"];
}