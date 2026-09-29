using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderStats;

/// <summary>
/// The admin dashboard's numbers (<c>GET /api/v1/orders/stats</c>, Admin panel S8, endpoint #63).
///
/// <para>
/// Bound with <c>[AsParameters]</c>, so every value-typed property is nullable — a non-nullable one
/// is a required query parameter, and omitting it fails binding with a 400 that blames a JSON body
/// the request does not have.
/// </para>
///
/// <para>
/// Uncached, like Catalog's category stats and the two S4 admin reads: an admin reads these straight
/// after changing something, and a correct cache would need a family bumped by every order write.
/// </para>
/// </summary>
public record GetOrderStatsQuery : IRequest<Result<OrderStatsDto>>
{
    /// <summary>Inclusive lower bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary><c>Day</c> (default), <c>Month</c> or <c>Year</c>, case-insensitive.</summary>
    public string? GroupBy { get; init; }
}
