using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Bulk;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkChangeProductCategory;

/// <summary>
/// Moves every named product into one category (admin panel S16, endpoint #45).
/// </summary>
/// <remarks>
/// A missing or deactivated target category refuses the <b>whole request</b> with <c>Category.NotFound</c>, not each
/// row: it is one fact about the request, and reporting it a thousand times would say nothing more. Answered as 400, like
/// <c>Category.ParentNotFound</c>: the route exists, and it is the body that names something missing. See
/// <see cref="BulkProductReport"/> for what a report guarantees.
/// </remarks>
public record BulkChangeProductCategoryCommand : IRequest<Result<BulkProductReport>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Product";

    // A batch has no one entity: each product it touched gets its own audit row, from the report.
    string? IAuditedCommand.AuditEntityId => null;

    // Each row carries the target, which is otherwise only in the request.
    IReadOnlyList<AuditedItem>? IAuditedCommand.AuditItemsFromResult(object? value)
        => (value as BulkProductReport)?.ToAuditItems(_ => new { CategoryId });

    public IReadOnlyList<Guid>? ProductIds { get; init; }

    public Guid CategoryId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => BulkProductCacheKeys.DetailKeysFor(ProductIds);

    // Once for the whole batch. It covers both categories' product pages, since every product list is in this family.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
