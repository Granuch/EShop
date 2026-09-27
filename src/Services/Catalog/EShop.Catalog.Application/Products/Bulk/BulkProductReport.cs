using EShop.BuildingBlocks.Application.Auditing;

namespace EShop.Catalog.Application.Products.Bulk;

/// <summary>
/// The answer to every bulk product action (admin panel S16, decision Q5a: synchronous, hard-capped, per-row report).
/// </summary>
/// <param name="Requested">How many products the request named.</param>
/// <param name="Succeeded">How many were changed, or already in the requested state.</param>
/// <param name="Failed">How many were refused. <c>Succeeded + Failed == Requested</c>.</param>
/// <param name="Items">One entry per requested product, <b>in request order</b>, so a client can zip it with what it sent.</param>
/// <remarks>
/// <b>Every <c>Succeeded</c> item is committed, and nothing a <c>Failed</c> item names was changed.</b> The whole batch is
/// one transaction: a refusal of one product is decided before anything is written, and a database failure fails the
/// request rather than the row, so a report is never returned for writes that did not happen.
/// </remarks>
public sealed record BulkProductReport(
    int Requested,
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkProductItemResult> Items)
{
    public static BulkProductReport From(IReadOnlyList<BulkProductItemResult> items)
    {
        var succeeded = items.Count(i => i.Succeeded);
        return new BulkProductReport(items.Count, succeeded, items.Count - succeeded, items);
    }

    /// <summary>One audit row per product, each with its own outcome — see <see cref="IAuditedCommand.AuditItemsFromResult"/>.</summary>
    public IReadOnlyList<AuditedItem> ToAuditItems(Func<Guid, object?>? detail = null)
        => Items
            .Select(i => new AuditedItem(i.ProductId.ToString(), i.ErrorCode, detail?.Invoke(i.ProductId)))
            .ToList();
}

/// <summary>One product's outcome in a <see cref="BulkProductReport"/>.</summary>
/// <param name="ErrorCode">
/// <c>Product.NotFound</c> for an id with no live product, or <c>DomainError</c> — the same code a single-product request
/// gets for the same refusal — when the product's own rules refused the change.
/// </param>
/// <param name="Error">Why, in words; our own message, never a framework's.</param>
public sealed record BulkProductItemResult(Guid ProductId, bool Succeeded, string? ErrorCode = null, string? Error = null)
{
    public static BulkProductItemResult Success(Guid productId) => new(productId, true);

    public static BulkProductItemResult Failure(Guid productId, string errorCode, string error)
        => new(productId, false, errorCode, error);
}
