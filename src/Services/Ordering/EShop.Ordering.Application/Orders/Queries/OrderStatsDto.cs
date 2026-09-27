using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Orders.Queries;

/// <summary>
/// What <c>GET /api/v1/orders/stats</c> answers (Admin panel S8, endpoint #63): the window's totals,
/// a per-status breakdown, and one bucket per period.
/// </summary>
/// <param name="From">The window echoed back, so a client can label a chart without re-parsing its own query.</param>
/// <param name="GroupBy">Likewise — the bucket size actually used, not the one requested.</param>
/// <param name="TotalOrders">Orders created in the window, whatever their status.</param>
/// <param name="GrossValue">The sum of their totals, whatever their status. Not revenue: see <paramref name="PaidRevenue"/>.</param>
/// <param name="PaidRevenue">
/// The sum over Paid, Shipped and Delivered orders — money taken and not given back. Refunded is
/// excluded because the money went back; Cancelled because it was never taken; Pending because it has
/// not been. Those three are reported separately rather than folded in, so a dashboard cannot show a
/// refund as income.
/// </param>
/// <param name="ByStatus">
/// One entry per <see cref="OrderStatus"/>, <b>including the statuses with no orders</b>. A client
/// charting six bars should not have to know which keys the server chose to omit.
/// </param>
public sealed record OrderStatsDto(
    DateTime? From,
    DateTime? To,
    OrderStatsGroupBy GroupBy,
    int TotalOrders,
    decimal GrossValue,
    decimal PaidRevenue,
    decimal RefundedValue,
    decimal CancelledValue,
    IReadOnlyList<OrderStatusBreakdown> ByStatus,
    IReadOnlyList<OrderStatsBucket> Buckets);

/// <summary>How many orders are in one status in the window, and what they are worth.</summary>
public sealed record OrderStatusBreakdown(OrderStatus Status, int Count, decimal Value);

/// <summary>
/// One period. <paramref name="PeriodStart"/> is the UTC instant the bucket begins — midnight of the
/// day, the first of the month, or January 1st — so buckets are directly comparable and sortable.
/// Periods with no orders are absent rather than zero-filled: the window is unbounded by default, and
/// zero-filling an open range has no end to fill to.
/// </summary>
public sealed record OrderStatsBucket(
    DateTime PeriodStart,
    int OrderCount,
    decimal GrossValue,
    decimal PaidRevenue);
