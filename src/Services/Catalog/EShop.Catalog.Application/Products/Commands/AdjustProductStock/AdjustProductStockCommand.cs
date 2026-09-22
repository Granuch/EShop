using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AdjustProductStock;

/// <summary>
/// Command to move a product's stock on its own (Admin panel S4), returning the new quantity.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed the only way to change stock was the full <c>PUT /products/{id}</c>, which
/// also rewrites price — so recording a delivery meant re-sending the price, and a stale admin form
/// silently reverted a price change someone else had just made.
/// </para>
/// <para>
/// <b>Exactly one of <see cref="Delta"/> and <see cref="Absolute"/> must be supplied</b>, and the
/// distinction is the point rather than a convenience. <c>Absolute</c> is a stock-take: the caller
/// counted the shelf and its answer wins whatever is stored. <c>Delta</c> is a movement: it composes
/// with concurrent changes, so two admins recording two deliveries both land. Offering only the
/// absolute form turns every delivery into a read-modify-write across an HTTP round trip, which
/// loses one of them silently.
/// </para>
/// </remarks>
public record AdjustProductStockCommand : IRequest<Result<int>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Product";

    string? IAuditedCommand.AuditEntityId => ProductId.ToString();

    public Guid ProductId { get; init; }

    /// <summary>Relative movement. Negative is a write-off. Zero is refused, never a no-op.</summary>
    public int? Delta { get; init; }

    /// <summary>Absolute quantity from a stock-take. Must not be negative.</summary>
    public int? Absolute { get; init; }

    /// <summary>
    /// Free text for the audit trail. Recorded in the command log by <c>LoggingBehavior</c> today;
    /// S15's audit log is what will persist it.
    /// </summary>
    public string? Reason { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16. Stock is projected into the list DTO and, since S4, is filterable through
    // ?StockBelow= — so a stock move can change which products a page contains, not merely what
    // they show. The family bump covers both.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
