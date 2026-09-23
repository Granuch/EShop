using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Bulk;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkPublishProducts;

/// <summary>
/// Publishes every named draft (admin panel S16, endpoint #42).
/// </summary>
/// <remarks>
/// Same rule as <c>POST /products/{id}/publish</c>: an already-published product is a success, not an error, so a retried batch does not report a failure for work the first attempt did. See <see cref="BulkProductReport"/> for what the answer guarantees.
/// </remarks>
public record BulkPublishProductsCommand : IRequest<Result<BulkProductReport>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Product";

    // A batch has no one entity: each product it touched gets its own audit row, from the report.
    string? IAuditedCommand.AuditEntityId => null;

    IReadOnlyList<AuditedItem>? IAuditedCommand.AuditItemsFromResult(object? value)
        => (value as BulkProductReport)?.ToAuditItems();

    public IReadOnlyList<Guid>? ProductIds { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => BulkProductCacheKeys.DetailKeysFor(ProductIds);

    // Once for the whole batch — the point of one command rather than one per product.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
