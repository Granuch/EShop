using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Bulk;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkUnpublishProducts;

/// <summary>
/// Withdraws every named product from the public catalog, returning it to draft (admin panel S16, endpoint #43).
/// </summary>
/// <remarks>
/// Same rule as <c>POST /products/{id}/unpublish</c>: an already-draft product is a success. See <see cref="BulkProductReport"/> for what the answer guarantees.
/// </remarks>
public record BulkUnpublishProductsCommand : IRequest<Result<BulkProductReport>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
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
