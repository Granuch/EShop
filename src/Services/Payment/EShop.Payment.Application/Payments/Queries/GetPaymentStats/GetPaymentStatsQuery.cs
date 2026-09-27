using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentStats;

/// <summary>
/// The admin payment dashboard's numbers (<c>GET /api/v1/payments/stats</c>, Admin panel S10, endpoint #68).
///
/// <para>
/// Bound with <c>[AsParameters]</c>, so every value-typed property is nullable — a non-nullable one is a required
/// query parameter, and omitting it fails binding with a 400 that blames a JSON body the request does not have.
/// </para>
///
/// <para>
/// Uncached, like Ordering's order stats and Catalog's category stats: an admin reads these straight after changing
/// something, and Payment has no cache at all to hang a family off.
/// </para>
/// </summary>
public sealed record GetPaymentStatsQuery : IRequest<Result<PaymentStatsDto>>
{
    /// <summary>Inclusive lower bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary><c>Day</c> (default), <c>Month</c> or <c>Year</c>, case-insensitive.</summary>
    public string? GroupBy { get; init; }

    /// <summary>
    /// The currency every figure is summed in, defaulting to <c>USD</c>.
    /// <para>This filter is the whole reason the stats read has one more parameter than the list. Every figure here is
    /// a sum over a money column that sits beside a currency column, and a sum across currencies is a number with no
    /// unit. Payment writes USD only (audit D4), so the default is the only value that exists today — and the day a
    /// second one appears, the dashboard reports one currency instead of silently adding them together.</para>
    /// </summary>
    public string? Currency { get; init; }

    public string EffectiveCurrency =>
        string.IsNullOrWhiteSpace(Currency) ? PaymentQueryEnums.DefaultCurrency : Currency.Trim().ToUpperInvariant();
}
