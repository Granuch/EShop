using EShop.BuildingBlocks.Domain;

namespace EShop.Payment.Domain.Entities;

/// <summary>
/// One recorded step in a payment's life (Admin panel S11, endpoint #66). Append-only: there is no mutator and no
/// delete path, because a timeline that can be edited is not one.
///
/// <para>
/// Written by <see cref="PaymentTransaction"/> itself, inside the transition that caused it — never by a consumer, a
/// webhook handler or a domain-event handler. Same reasoning as Ordering's <c>OrderStatusHistory</c> (Admin panel S9),
/// and it matters more here: a payment is written by six different callers (both consumers, the webhook processor, the
/// refunder, and the settle and create-intent handlers), so a timeline assembled by any of them would be missing every
/// transition the others make. Appending here means the row and the status move in one <c>SaveChanges</c>, so they can
/// only both exist or both not.
/// </para>
///
/// <para>
/// <b>Payment raises no domain events at all</b> — it publishes integration events through the outbox instead — so an
/// event-fed timeline was never even available as a wrong answer.
/// </para>
///
/// <para>
/// <b>The actor comes from <see cref="Entity{TId}.CreatedBy"/>, which <c>BaseDbContext.SetAuditFields</c> stamps from
/// <c>ICurrentUserContext</c></b> — the same mechanism Ordering's <c>OrderStatusHistory</c> uses, so no domain method
/// here had to grow an <c>actor</c> argument. It works only because Payment registers <c>ICurrentUserContext</c>,
/// which until 2026-09-20 it was the one service of six not to do; this entity was written without an actor for
/// exactly that reason, and gained one as soon as the registration landed. An operator's offline settlement therefore
/// records their user id and a consumer's or a webhook's transition records <c>"system"</c>.
/// </para>
/// </summary>
public class PaymentEvent : Entity<Guid>
{
    /// <summary>Matches <c>PaymentTransaction.ErrorMessage</c>'s column, which is where the longest details come
    /// from (Stripe's decline reasons, an operator's refund note).</summary>
    public const int MaxDetailLength = 500;

    /// <summary>Stripe's own event ids are short; the column matches <c>ProcessedStripeWebhookEvents.EventId</c>.</summary>
    public const int MaxStripeEventIdLength = 200;

    public Guid PaymentTransactionId { get; private set; }

    /// <summary>Who moved the payment: Stripe, or this service.</summary>
    public PaymentEventKind Kind { get; private set; }

    /// <summary>
    /// The Stripe event that caused this row, when one did. Null for every <see cref="PaymentEventKind.Transition"/>
    /// row, and for a webhook-driven row recorded before the id was known.
    /// </summary>
    public string? StripeEventId { get; private set; }

    /// <summary>
    /// The status before, or <c>null</c> for the row a payment's creation writes — a payment comes into existence
    /// Pending (or Cancelled, for the placeholder a cancellation leaves), and there is no previous state to name.
    /// </summary>
    public PaymentStatus? FromStatus { get; private set; }

    /// <summary>
    /// The status after. <b>Never null</b>, and equal to <see cref="FromStatus"/> when the event changed something
    /// other than the status — a declined card, a refund note, a late webhook that was correctly ignored. A nullable
    /// "no transition" marker was rejected: with creation already using a null <see cref="FromStatus"/>, a null here
    /// would have given the pair three readings and every consumer of the timeline a footnote.
    /// </summary>
    public PaymentStatus ToStatus { get; private set; }

    /// <summary>What happened, in our own words. Truncated to <see cref="MaxDetailLength"/>, because some of it comes
    /// from Stripe and some from an operator.</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>
    /// When it happened, as the transition itself saw the clock.
    ///
    /// <para>
    /// Deliberately <b>not</b> <see cref="Entity{TId}.CreatedAt"/>, which this entity also carries:
    /// <c>BaseDbContext.SetAuditFields</c> overwrites <c>CreatedAt</c> with "now" on every insert whatever the entity
    /// held, so it records when the row was <i>saved</i> rather than when the payment moved. For a webhook replayed
    /// days after it arrived those are emphatically not the same instant — and the timeline is read in
    /// <c>OccurredAt</c> order for that reason.
    /// </para>
    /// </summary>
    public DateTime OccurredAt { get; private set; }

    private PaymentEvent() { }

    /// <summary>
    /// <c>internal</c> so only the aggregate can write the timeline. A public constructor would let a handler record a
    /// transition that never happened.
    /// </summary>
    internal PaymentEvent(
        Guid paymentTransactionId,
        PaymentEventKind kind,
        PaymentStatus? fromStatus,
        PaymentStatus toStatus,
        string detail,
        DateTime occurredAt,
        string? stripeEventId)
    {
        Id = Guid.NewGuid();
        PaymentTransactionId = paymentTransactionId;
        Kind = kind;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Detail = Truncate(detail, MaxDetailLength);
        OccurredAt = occurredAt;
        StripeEventId = string.IsNullOrWhiteSpace(stripeEventId)
            ? null
            : Truncate(stripeEventId.Trim(), MaxStripeEventIdLength);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Why a <see cref="PaymentEvent"/> exists. Stored as its name, like every other enum in this service, so a row stays
/// readable after the enum is reordered.
/// </summary>
public enum PaymentEventKind
{
    /// <summary>This service moved the payment: a consumer, an operator, the simulator, or <c>/create-intent</c>.</summary>
    Transition,

    /// <summary>A Stripe webhook delivery moved it, or was applied and correctly changed nothing.</summary>
    Webhook
}
