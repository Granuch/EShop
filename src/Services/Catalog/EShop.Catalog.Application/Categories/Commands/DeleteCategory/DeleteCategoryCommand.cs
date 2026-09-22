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
    /// A4 (Admin panel S5): the <c>"categories:all"</c> literal was removed — the tree read's key
    /// now embeds <c>includeInactive</c> and nothing writes that fixed string any more, so evicting
    /// it removes nothing and logs success. The family bump below replaces it. Note a delete is
    /// exactly the case where getting this wrong is visible: the deactivated category must leave
    /// the anonymous tree immediately, and must appear in the admin one.
    /// </remarks>
    public IEnumerable<string> CacheKeysToInvalidate => [CategoryCacheKeys.Detail(Id)];

    public IEnumerable<string> CacheFamiliesToInvalidate => [CategoryCacheFamilies.CategoryList];
}