using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Bulk;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkUpdateProductPrices;

/// <summary>
/// Sets a new list price on each named product (admin panel S16, endpoint #46).
/// </summary>
/// <remarks>
/// <para>
/// <b>An absolute price per product, not a percentage.</b> A "+10 %" mode would have to pick a rounding rule for every
/// price it touches, and every rule is wrong for somebody (half-up, banker's, charm pricing to .99). The client already
/// holds the prices it is editing and decides the new ones; this endpoint applies exactly what it was sent.
/// </para>
/// <para>
/// Each price goes through <c>Product.UpdatePrice</c>, so it follows every rule a single <c>PUT /products/{id}</c> does —
/// in particular a price at or below an active discount is refused for that product (<c>DomainError</c>, with the
/// domain's own message) rather than silently dropping the discount, and a moved customer-facing price raises
/// <c>ProductPriceChangedEvent</c> for Basket exactly as one edit would.
/// </para>
/// </remarks>
public record BulkUpdateProductPricesCommand : IRequest<Result<BulkProductReport>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Product";

    // A batch has no one entity: each product it touched gets its own audit row, from the report.
    string? IAuditedCommand.AuditEntityId => null;

    // Each row carries the price that product was sent, which is otherwise only in the (truncated) request.
    IReadOnlyList<AuditedItem>? IAuditedCommand.AuditItemsFromResult(object? value)
    {
        if (value is not BulkProductReport report)
            return null;

        var prices = (Items ?? []).ToDictionary(i => i.ProductId, i => i.Price);
        return report.ToAuditItems(id => new { Price = prices.GetValueOrDefault(id) });
    }

    public IReadOnlyList<BulkProductPriceItem>? Items { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => BulkProductCacheKeys.DetailKeysFor(Items?.Select(i => i.ProductId));

    // Once for the whole batch. The price sort and filters of every list read the changed prices.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}

/// <summary>One product's new list price.</summary>
public record BulkProductPriceItem
{
    public Guid ProductId { get; init; }
    public decimal Price { get; init; }
}
