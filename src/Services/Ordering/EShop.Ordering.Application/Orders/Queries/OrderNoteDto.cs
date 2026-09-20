using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Orders.Queries;

/// <summary>
/// One operator note (Admin panel S9, endpoint #61). Admin-only, like the endpoint that returns it —
/// see <see cref="OrderNote"/> for why a note must never reach the customer whose order it is about.
/// </summary>
/// <param name="CreatedAt">When the note was written; the list is returned newest first.</param>
public sealed record OrderNoteDto(
    Guid Id,
    Guid OrderId,
    string AuthorId,
    string AuthorName,
    string Body,
    DateTime CreatedAt);

/// <summary>
/// One step of an order's timeline (Admin panel S9, endpoint #62), oldest first.
/// </summary>
/// <param name="FromStatus">
/// <c>null</c> on the first row only — an order is created Pending, with no state before it.
/// </param>
/// <param name="Reason">Only a cancellation carries one today.</param>
/// <param name="ActorId">
/// Who caused the transition: the authenticated operator's id for an admin action, <c>"system"</c> for
/// one driven by an inbound integration event (a payment succeeding or being refunded), and
/// <c>null</c> only for rows written before an actor could be resolved. It is
/// <c>OrderStatusHistory.CreatedBy</c>, which <c>BaseDbContext</c> stamps — not a field any caller can
/// set.
/// </param>
public sealed record OrderStatusHistoryDto(
    Guid Id,
    Guid OrderId,
    OrderStatus? FromStatus,
    OrderStatus ToStatus,
    string? Reason,
    string? ActorId,
    DateTime OccurredAt);
