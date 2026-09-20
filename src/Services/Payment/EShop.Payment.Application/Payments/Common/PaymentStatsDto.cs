using EShop.Payment.Domain.Interfaces;

namespace EShop.Payment.Application.Payments.Common;

/// <summary>
/// What <c>GET /api/v1/payments/stats</c> answers (Admin panel S10, endpoint #68).
///
/// <para>
/// It exists rather than serializing <see cref="PaymentStats"/> straight out of Domain for one reason: the status has
/// to reach the client as the same upper-case string <see cref="PaymentDto.Status"/> uses. Serializing the enum would
/// put a <i>number</i> in the response — System.Text.Json's default — so one payment endpoint would report
/// <c>"SUCCESS"</c> and its dashboard <c>2</c>, for the same fact.
/// </para>
/// </summary>
/// <param name="Currency">The currency every figure below is in, echoed so a chart can label its axis.</param>
/// <param name="GrossAmount">The sum of every payment in the window, whatever its status. Not revenue.</param>
/// <param name="CapturedRevenue">
/// The sum over successful payments alone — money taken and not given back. Refunded, failed, cancelled and in-flight
/// amounts are reported separately rather than folded in, so no dashboard can show a refund as income.
/// </param>
/// <param name="ByStatus">One entry per payment status, <b>including the statuses with no payments</b>.</param>
public sealed record PaymentStatsDto(
    DateTime? From,
    DateTime? To,
    string Currency,
    string GroupBy,
    int TotalPayments,
    decimal GrossAmount,
    decimal CapturedRevenue,
    decimal RefundedAmount,
    decimal FailedAmount,
    IReadOnlyList<PaymentStatusBreakdownDto> ByStatus,
    IReadOnlyList<PaymentStatsBucketDto> Buckets);

/// <inheritdoc cref="PaymentStatusBreakdown"/>
public sealed record PaymentStatusBreakdownDto(string Status, int Count, decimal Amount);

/// <inheritdoc cref="PaymentStatsBucket"/>
public sealed record PaymentStatsBucketDto(
    DateTime PeriodStart,
    int PaymentCount,
    decimal GrossAmount,
    decimal CapturedRevenue,
    decimal RefundedAmount);

internal static class PaymentStatsMapping
{
    public static PaymentStatsDto ToDto(this PaymentStats stats) => new(
        stats.From,
        stats.To,
        stats.Currency,
        stats.GroupBy.ToString(),
        stats.TotalPayments,
        stats.GrossAmount,
        stats.CapturedRevenue,
        stats.RefundedAmount,
        stats.FailedAmount,
        stats.ByStatus
            .Select(s => new PaymentStatusBreakdownDto(s.Status.ToString().ToUpperInvariant(), s.Count, s.Amount))
            .ToList(),
        stats.Buckets
            .Select(b => new PaymentStatsBucketDto(
                b.PeriodStart, b.PaymentCount, b.GrossAmount, b.CapturedRevenue, b.RefundedAmount))
            .ToList());
}
