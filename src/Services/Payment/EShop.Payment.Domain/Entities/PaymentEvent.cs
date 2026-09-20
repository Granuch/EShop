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
/// <b>Not an <c>Entity&lt;Guid&gt;</c>, unlike Ordering's <c>OrderStatusHistory</c>, and the difference is a fact
/// about this service rather than a style choice.</b> That base class brings <c>CreatedAt</c>/<c>CreatedBy</c>, and
/// the actor half only works because <c>BaseDbContext.SetAuditFields</c> stamps <c>CreatedBy</c> from
/// <c>ICurrentUserContext</c> — which <b>Payment alone of the six services does not register</b> (checked
/// 2026-09-20: Basket, Catalog, Identity, Notification and Ordering each call
/// <c>AddScoped&lt;ICurrentUserContext, HttpCurrentUserContext&gt;</c> and Payment does not). Deriving anyway would
/// put an <c>ActorId</c> on the timeline that is null for every row ever written, which is worse than having no
/// actor: a reader would conclude nobody did it. <c>OccurredAt</c> makes <c>CreatedAt</c> redundant here in any case.
/// </para>
/// </summary>
public class PaymentEvent
{
    /// <summary>Matches <c>PaymentTransaction.ErrorMessage</c>'s column, which is where the longest details come
    /// from (Stripe's decline reasons, an operator's refund note).</summary>
    public const int MaxDetailLength = 500;

    /// <summary>Stripe's own event ids are short; the column matches <c>ProcessedStripeWebhookEvents.EventId</c>.</summary>
    public const int MaxStripeEventIdLength = 200;

    public Guid Id { get; private set; }

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
    /// Deliberately not named <c>CreatedAt</c>: <c>BaseDbContext.SetAuditFields</c> overwrites any writable property
    /// of that name with "now" on every insert, whatever the entity held, so it would record when the row was saved
    /// rather than when the payment moved. For a webhook replayed days later those are emphatically not the same
    /// instant.
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
