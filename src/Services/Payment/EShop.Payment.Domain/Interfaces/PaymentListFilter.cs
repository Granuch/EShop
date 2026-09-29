using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// Everything the admin payment list, the export and the stats window can narrow on (Admin panel S10, endpoints
/// #65/#68/#69). One record rather than nine more parameters: each would land before the trailing
/// <see cref="System.Threading.CancellationToken"/>, and the repo has already paid for that three times — the call
/// sites and the Moq setups both report a CS1503 naming the cancellation token, the one parameter that did not change.
/// Mirrors Ordering's <c>OrderListFilter</c> and Catalog's <c>ProductListFilter</c>.
/// </summary>
/// <param name="Statuses">
/// Empty means "every status". More than one is a union, so the panel's status multi-select is one request. There is
/// deliberately no second single-valued <c>status</c> parameter: this list is new, so unlike Ordering's there is no
/// older caller to keep working, and two filters over one column can be given contradictory values.
/// </param>
/// <param name="UserId">Exact match. Not a substring search: the admin list has no free-text search at all, because
/// <c>EF.Functions.ILike</c> does not run on the EF InMemory provider the Payment HTTP suite uses, so a search filter
/// could not be covered by the tests that cover every other filter here.</param>
/// <param name="OrderId">Exact match. A payment's order is unique, so this answers at most one row.</param>
/// <param name="Currency">
/// Used by the stats read, not by the list. A sum over a money column whose currency column is unconstrained is a
/// number with no unit; every row Payment writes today is USD (audit D4), so filtering changes nothing now and cannot
/// silently mix later. The list needs no such filter — each of its rows carries its own currency.
/// </param>
/// <param name="From">Inclusive lower bound on <c>CreatedAt</c> (UTC).</param>
/// <param name="To">Inclusive upper bound on <c>CreatedAt</c> (UTC).</param>
public sealed record PaymentListFilter(
    IReadOnlyCollection<PaymentStatus>? Statuses = null,
    string? UserId = null,
    Guid? OrderId = null,
    PaymentMethodType? PaymentMethod = null,
    string? Currency = null,
    DateTime? From = null,
    DateTime? To = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null);

/// <summary>The period one <c>GET /api/v1/payments/stats</c> bucket covers.</summary>
/// <remarks>
/// No <c>Week</c>, for the reason Ordering's <c>OrderStatsGroupBy</c> gives: a week has no single definition, so the
/// API would have to pick one silently. Day, month and year each mean one thing, and each groups on plain
/// <c>CreatedAt</c> date components — Npgsql 10's <c>EF.Functions</c> exposes no <c>date_trunc</c>, so anything else
/// would need raw SQL.
/// </remarks>
public enum PaymentStatsGroupBy
{
    Day,
    Month,
    Year
}

/// <summary>
/// The numbers behind the admin payment dashboard (endpoint #68), over payments created inside the window.
/// </summary>
/// <param name="Currency">The currency every figure below is in, echoed so a chart can label its axis.</param>
/// <param name="GrossAmount">The sum of every payment in the window, whatever its status. Not revenue.</param>
/// <param name="CapturedRevenue">
/// The sum over <see cref="PaymentStatus.Success"/> alone — money taken and not given back. Refunded is excluded
/// because it went back, Cancelled and Failed because it was never taken, Pending and Processing because it has not
/// been yet. They are reported on their own rather than folded in, so no dashboard can show a refund as income.
/// </param>
/// <param name="ByStatus">
/// One entry per <see cref="PaymentStatus"/>, <b>including the statuses with no payments</b>. A client charting six
/// bars should not have to know which keys the server chose to omit.
/// </param>
public sealed record PaymentStats(
    DateTime? From,
    DateTime? To,
    string Currency,
    PaymentStatsGroupBy GroupBy,
    int TotalPayments,
    decimal GrossAmount,
    decimal CapturedRevenue,
    decimal RefundedAmount,
    decimal FailedAmount,
    IReadOnlyList<PaymentStatusBreakdown> ByStatus,
    IReadOnlyList<PaymentStatsBucket> Buckets);

/// <summary>How many payments are in one status in the window, and what they are worth.</summary>
public sealed record PaymentStatusBreakdown(PaymentStatus Status, int Count, decimal Amount);

/// <summary>
/// One period. <paramref name="PeriodStart"/> is the UTC instant the bucket begins — midnight of the day, the first of
/// the month, or January 1st — so buckets are directly comparable and sortable. Periods with no payments are absent
/// rather than zero-filled: the window is unbounded by default, and an open range has no end to fill to.
/// </summary>
public sealed record PaymentStatsBucket(
    DateTime PeriodStart,
    int PaymentCount,
    decimal GrossAmount,
    decimal CapturedRevenue,
    decimal RefundedAmount);
