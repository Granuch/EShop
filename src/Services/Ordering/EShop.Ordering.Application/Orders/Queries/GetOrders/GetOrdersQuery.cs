using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries.GetOrders;

/// <summary>
/// The admin order list (<c>GET /api/v1/orders</c>), extended in Admin panel S8 from
/// <c>status</c> + paging to the filter set an operator actually needs.
///
/// <para>
/// <b>Every value-typed property here is nullable, and that is load-bearing.</b> Bound with
/// <c>[AsParameters]</c>, a non-nullable value type is a <i>required</i> parameter: a plain
/// <c>bool IsDescending</c> would make every request that omits it fail binding with a 400 claiming
/// "the request body is not valid JSON", on a GET that has no body. Hence the <c>Effective*</c>
/// accessors, which is also where the defaults live.
/// </para>
///
/// <para>
/// Not cached, deliberately — see Ordering's <c>CLAUDE.md</c>: every order write evicts
/// <c>OrderCacheKeys.Order(id)</c> and bumps <c>UserOrders(userId)</c>, and neither names a global
/// list, so a cached admin list would be invalidated by nothing while every eviction logged success.
/// Caching it needs a new versioned family plus a bump in every write, not an interface.
/// </para>
/// </summary>
public record GetOrdersQuery : IRequest<Result<PagedResult<OrderDto>>>
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    /// <summary>
    /// The single-status filter this endpoint has always accepted. Kept because removing it would
    /// break every existing caller; it folds into <see cref="RequestedStatusNames"/> together with
    /// <see cref="Statuses"/> rather than being a second, competing filter.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>Repeatable: <c>?statuses=Paid&amp;statuses=Shipped</c>. A union, not an intersection.</summary>
    public string[]? Statuses { get; init; }

    /// <summary>Substring over the user id and the payment intent id; an exact match on the order id.</summary>
    public string? Search { get; init; }

    /// <summary>Inclusive lower bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? To { get; init; }

    public decimal? MinTotal { get; init; }
    public decimal? MaxTotal { get; init; }

    /// <summary>One of <see cref="OrderSortBy"/>, case-insensitive. Defaults to <c>CreatedAt</c>.</summary>
    public string? SortBy { get; init; }

    /// <summary>Defaults to <c>true</c>: newest first is what the panel opens on.</summary>
    public bool? IsDescending { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
    public bool EffectiveIsDescending => IsDescending ?? true;

    /// <summary>
    /// The union of <see cref="Status"/> and <see cref="Statuses"/>, as written by the caller. The
    /// validator checks these names; the handler parses them only once they are known to be valid.
    /// </summary>
    public IReadOnlyList<string> RequestedStatusNames
    {
        get
        {
            var names = new List<string>();

            if (Statuses is { Length: > 0 })
                names.AddRange(Statuses.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

            if (!string.IsNullOrWhiteSpace(Status))
                names.Add(Status.Trim());

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
