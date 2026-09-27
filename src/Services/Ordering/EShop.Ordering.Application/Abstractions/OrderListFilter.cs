using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Abstractions;

/// <summary>
/// Everything the admin order list can narrow on (Admin panel S8). One record rather than a growing
/// parameter list: <c>GetOrdersAsync</c> took <c>(status, pageNumber, pageSize, ct)</c>, and each new
/// filter added before the trailing <see cref="System.Threading.CancellationToken"/> would break every
/// positional call site and every Moq setup with a CS1503 naming the cancellation token — the one
/// parameter that did not change.
/// </summary>
/// <param name="Statuses">
/// Empty means "every status". More than one is a union, so the panel's status multi-select is one
/// request. The single <c>?status=</c> parameter the endpoint has always accepted folds into this set.
/// </param>
/// <param name="Search">
/// Case-insensitive substring over <c>UserId</c> and <c>PaymentIntentId</c>, plus an exact match on
/// <c>Id</c> when the term parses as a Guid. Deliberately not over item product names: that would need
/// a join per row and make "find this customer's order" slower for the case it is actually used for.
/// </param>
/// <param name="From">Inclusive lower bound on <c>CreatedAt</c> (UTC), matching Catalog and Identity.</param>
/// <param name="To">Inclusive upper bound on <c>CreatedAt</c> (UTC).</param>
/// <param name="MinTotal">Inclusive lower bound on <c>TotalPrice</c>.</param>
/// <param name="MaxTotal">Inclusive upper bound on <c>TotalPrice</c>.</param>
public sealed record OrderListFilter(
    IReadOnlyCollection<OrderStatus>? Statuses = null,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null,
    decimal? MinTotal = null,
    decimal? MaxTotal = null);

/// <summary>
/// The columns the admin list may be sorted by. <c>Id</c> is always the tie-break and is not offered:
/// it is not a meaningful order, and offset paging over a non-unique sort is nondeterministic.
/// </summary>
public enum OrderSortBy
{
    CreatedAt,
    TotalPrice,
    Status
}

/// <summary>The period one <c>GET /api/v1/orders/stats</c> bucket covers.</summary>
/// <remarks>
/// There is deliberately no <c>Week</c>. A week has no single definition (ISO weeks start Monday,
/// much of the world's reporting starts Sunday), so the API would have to pick one silently and be
/// wrong for half its callers; day, month and year each mean exactly one thing. Day/month/year also
/// group on plain <c>CreatedAt</c> date components, which Npgsql translates without help —
/// <c>EF.Functions</c> in Npgsql 10 has no <c>date_trunc</c>, so a week bucket would have to be raw
/// SQL or client-side arithmetic over every row.
/// </remarks>
public enum OrderStatsGroupBy
{
    Day,
    Month,
    Year
}
