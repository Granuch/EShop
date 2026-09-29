using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public record CreateCategoryCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Category";

    string? IAuditedCommand.AuditEntityId => null;

    string? IAuditedCommand.AuditEntityIdFromResult(object? value) => value is Guid id ? id.ToString() : null;

    public string Name { get; init; } = string.Empty;

    public string? Slug { get; init; }

    public Guid? ParentCategoryId { get; init; }

    /// <summary>M11. Could only be set by a later PUT before Stage 8.</summary>
    public string? Description { get; init; }

    /// <summary>M11. Defaults to 0. Nullable so an omitted value binds rather than failing.</summary>
    public int? DisplayOrder { get; init; }

    /// <summary>
    /// Empty on purpose. A new child appears in the cached detail of its parent and of every
    /// ancestor above it (F-37: the detail carries the whole subtree), and both category reads are
    /// versioned in <see cref="CategoryCacheFamilies.CategoryList"/>, so the family bump below is
    /// what evicts them. An exact key here would match no stored entry and log success.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [];

    public IEnumerable<string> CacheFamiliesToInvalidate => [CategoryCacheFamilies.CategoryList];
}
