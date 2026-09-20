using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderNotes;

/// <summary>
/// One order's operator notes (<c>GET /api/v1/orders/{id}/notes</c>, Admin panel S9, endpoint #61).
///
/// <para>
/// Uncached, like the S4/S5/S8 admin reads: an operator reads the thread immediately after adding to
/// it, and a cache would need an entry evicted by a write that touches nothing else. Unpaged, because
/// <see cref="Domain.Entities.Order.MaxNotes"/> bounds the result at the write side rather than
/// truncating it at the read side.
/// </para>
/// </summary>
public record GetOrderNotesQuery : IRequest<Result<IReadOnlyList<OrderNoteDto>>>
{
    public Guid OrderId { get; init; }
}
