using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Entities;

/// <summary>
/// One recorded step of an order's state machine (Admin panel S9, endpoint #62). Append-only: there is
/// no mutator and no delete path, because a timeline that can be edited is not one.
///
/// <para>
/// Written by <see cref="Order"/> itself, inside the transition that caused it — never by a consumer
/// or a domain-event handler. That is not a style preference. A domain event in this repo is written
/// to <c>outbox_messages</c> and its handler runs later, in the outbox processor's own scope and its
/// own transaction (see the root guide); a history fed that way would be missing every row whose
/// transition rolled back *after* the event was enqueued, and would carry rows for transitions that
/// never committed. Appending here means the row and the status move in one <c>SaveChanges</c>, so
/// they can only both exist or both not.
/// </para>
/// <para>
/// Two of the six transitions raise no domain event at all (<see cref="Order.Deliver"/> and
/// <see cref="Order.Refund"/>), so an event-fed history could not have been complete even in
/// principle.
/// </para>
/// </summary>
public class OrderStatusHistory : Entity<Guid>
{
    /// <summary>Matches <c>Order.CancellationReason</c>'s column, which is where every reason comes from today.</summary>
    public const int MaxReasonLength = 500;

    public Guid OrderId { get; private set; }

    /// <summary>
    /// The status before the transition, or <c>null</c> for the row an order's creation writes — an
    /// order comes into existence Pending, and there is no previous state to name.
    /// </summary>
    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    /// <summary>
    /// Why, when the transition carries a reason. Only cancellation does today; every other row leaves
    /// it null rather than inventing prose the domain does not have.
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// When the transition happened, set by the aggregate at the moment it happened.
    ///
    /// <para>
    /// Deliberately not <see cref="Entity{TId}.CreatedAt"/>: <c>BaseDbContext.SetAuditFields</c>
    /// overwrites <c>CreatedAt</c> with "now" on every insert whatever the entity held, so it records
    /// when the row was saved rather than when the order moved. For a timeline those are the same
    /// instant today and would stop being the same the first time a transition is recorded outside its
    /// own request.
    /// </para>
    /// </summary>
    public DateTime OccurredAt { get; private set; }

    private OrderStatusHistory() { }

    /// <summary>
    /// <c>internal</c> so only the aggregate can write history. A public constructor would let a
    /// handler record a transition that never happened.
    /// </summary>
    internal OrderStatusHistory(Guid orderId, OrderStatus? fromStatus, OrderStatus toStatus, string? reason)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        OccurredAt = DateTime.UtcNow;
    }
}
