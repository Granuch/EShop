using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.DeleteCategory;

public record DeleteCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Category";

    string? IAuditedCommand.AuditEntityId => Id.ToString();

    public Guid Id { get; init; }

    /// <remarks>
    /// Empty on purpose: both category reads are versioned in
    /// <see cref="CategoryCacheFamilies.CategoryList"/> (the detail since F-37, frontend-contracts R5,
    /// because it now embeds the whole subtree), so the family bump below evicts this category's own
    /// entry and every ancestor's. Note a delete is exactly the case where getting this wrong is
    /// visible: the deactivated category must leave the anonymous tree and its ancestors' details
    /// immediately, and must appear in the admin tree.
    /// </remarks>
    public IEnumerable<string> CacheKeysToInvalidate => [];

    public IEnumerable<string> CacheFamiliesToInvalidate => [CategoryCacheFamilies.CategoryList];
}