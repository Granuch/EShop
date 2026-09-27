using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderStatusHistory;

/// <summary>
/// One order's status timeline (<c>GET /api/v1/orders/{id}/history</c>, Admin panel S9, endpoint #62),
/// oldest first.
///
/// <para>
/// Uncached and unpaged: the state machine has six states, so a timeline is bounded by construction at
/// a handful of rows, and an operator reads it straight after driving a transition.
/// </para>
/// </summary>
public record GetOrderStatusHistoryQuery : IRequest<Result<IReadOnlyList<OrderStatusHistoryDto>>>
{
    public Guid OrderId { get; init; }
}
