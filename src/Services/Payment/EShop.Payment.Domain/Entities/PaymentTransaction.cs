using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Payment.Domain.Entities;

/// <summary>
/// The payment for one order.
/// <para>Payment audit Stage 7 (H5). The status changes only through the methods below, each of which states the
/// transitions it allows. They encode the rules Stages 1–6 settled, which used to be spread across six writers, each
/// with its own guard list: <c>OrderCreatedConsumer</c>, <c>OrderCancelledConsumer</c>, <c>StripeWebhookProcessor</c>,
/// <c>PaymentRefunder</c>, and the settle and create-intent handlers.</para>
/// <para>The setters are <c>internal</c>, so Application and Infrastructure cannot build a payment in an invalid state.
/// The Payment test projects (<c>InternalsVisibleTo</c>) may still seed any state directly.</para>
/// <para>A call that the entity refuses throws <see cref="DomainException"/>. Callers check first, so it means a
/// defect. The webhook methods instead return false for an event that changes nothing, because a repeated or late Stripe
/// event is normal.</para>
/// </summary>
public class PaymentTransaction
{
    internal PaymentTransaction()
    {
    }

    public Guid Id { get; internal set; }
    public Guid OrderId { get; internal set; }
    public string UserId { get; internal set; } = string.Empty;
    /// <summary>
    /// The Stripe customer this payment's intent was created under. Nothing reads it (Payment audit D12). It is the
    /// payment's own record: the per-user <c>PaymentCustomers</c> mapping is not, since that mapping can change.
    /// </summary>
    public string? StripeCustomerId { get; internal set; }
    public decimal Amount { get; internal set; }

    /// <summary>
    /// When the order's total became <see cref="Amount"/>, as Ordering reported it in <c>OrderTotalChangedEvent</c>
    /// (frontend-contracts F-47). <c>null</c> while the amount is still the total the order was created with.
    /// <para>It exists only to order competing revisions: two changes to one order can be consumed in either order
    /// (concurrent consumers, a retried message), and a revision no newer than this one is ignored rather than
    /// allowed to put an older total back.</para>
    /// </summary>
    public DateTime? AmountAsOf { get; internal set; }

    public string Currency { get; internal set; } = "USD";
    public PaymentMethodType PaymentMethod { get; internal set; } = PaymentMethodType.Mock;
    public string PaymentIntentId { get; internal set; } = string.Empty;
    public string? StripeStatus { get; internal set; }
    public PaymentStatus Status { get; internal set; }
    public string? ErrorMessage { get; internal set; }
    public DateTime CreatedAt { get; internal set; }
    public DateTime? ProcessedAt { get; internal set; }
    public DateTime? UpdatedAt { get; internal set; }
    public uint Version { get; internal set; }

    private readonly List<PaymentEvent> _events = new();

    /// <summary>
    /// The timeline this payment has written during the current operation (Admin panel S11, endpoint #66).
    /// <para><b>Write-side only.</b> No repository read <c>Include</c>s it, so on a payment loaded from the database
    /// this collection is empty whatever the table holds. The read path is
    /// <c>IPaymentQueryService.GetEventsAsync</c>. Anything that needs to count or inspect stored events must query
    /// them — <c>_events.Count</c> would compare against zero forever, with nothing failing.</para>
    /// </summary>
    public IReadOnlyCollection<PaymentEvent> Events => _events.AsReadOnly();

    /// <summary>
    /// Appends one timeline row. Every method below that changes something calls this and nothing else does; see
    /// <see cref="PaymentEvent"/> for why the aggregate writes its own history.
    /// </summary>
    private void Record(
        PaymentEventKind kind,
        PaymentStatus? from,
        PaymentStatus to,
        string detail,
        DateTime occurredAt,
        string? stripeEventId = null)
    {
        // A timeline is read in order, so the rows one operation writes need distinct instants. Two of them routinely
        // share the clock — a refund and its reason are recorded in the same save, and DateTime.UtcNow is not
        // guaranteed to move between two statements — and ordering the read by (OccurredAt, Id) would then put them in
        // the order of two random GUIDs. The nudge is a tick, i.e. 100ns.
        if (_events.Count > 0 && occurredAt <= _events[^1].OccurredAt)
        {
            occurredAt = _events[^1].OccurredAt.AddTicks(1);
        }

        _events.Add(new PaymentEvent(Id, kind, from, to, detail, occurredAt, stripeEventId));
    }

    /// <summary>
    /// The Pending payment for a new order (Payment audit D1), in USD (D4). With <see cref="PaymentMethodType.Stripe"/>
    /// the customer pays through <c>/create-intent</c>; with <see cref="PaymentMethodType.Mock"/> the simulator settles it.
    /// </summary>
    public static PaymentTransaction RecordForOrder(
        Guid orderId,
        string userId,
        decimal amount,
        PaymentMethodType method,
        DateTime now)
    {
        if (method == PaymentMethodType.None)
        {
            throw new DomainException("An order's payment is made through the simulator or Stripe.");
        }

        if (amount < 0)
        {
            throw new DomainException($"A payment cannot be for a negative amount ({amount}).");
        }

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = userId,
            Amount = amount,
            Currency = "USD",
            PaymentMethod = method,
            Status = PaymentStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        payment.Record(
            PaymentEventKind.Transition,
            null,
            PaymentStatus.Pending,
            $"Payment of {amount} USD recorded for the order, to be settled by {method}.",
            now);

        return payment;
    }

    /// <summary>
    /// The record left when a cancellation overtakes <c>OrderCreatedEvent</c> (Ordering audit Stage 9). It is final, so the
    /// late event never charges the order.
    /// </summary>
    public static PaymentTransaction RecordCancelledBeforeCreation(Guid orderId, string userId, string note, DateTime now)
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = userId,
            Amount = 0m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.None,
            Status = PaymentStatus.Cancelled,
            ErrorMessage = note,
            CreatedAt = now,
            ProcessedAt = now,
            UpdatedAt = now
        };

        payment.Record(PaymentEventKind.Transition, null, PaymentStatus.Cancelled, note, now);

        return payment;
    }

    /// <summary>
    /// The simulator is about to settle the payment. Allowed for a Pending payment with no intent: a new order with Stripe
    /// off, or an admin settling a Stripe payment the customer never started (D2), which then becomes a Mock payment. Also
    /// allowed for a simulated payment already in flight, which <c>OrderCreatedConsumer</c> resumes on redelivery.
    /// </summary>
    public void StartSimulated(DateTime now)
    {
        var resuming = Status == PaymentStatus.Processing && PaymentMethod == PaymentMethodType.Mock;
        if ((Status != PaymentStatus.Pending && !resuming) || !string.IsNullOrEmpty(PaymentIntentId))
        {
            throw Refused(nameof(StartSimulated));
        }

        var previous = Status;
        PaymentMethod = PaymentMethodType.Mock;
        Status = PaymentStatus.Processing;
        UpdatedAt = now;

        Record(
            PaymentEventKind.Transition,
            previous,
            Status,
            resuming ? "Simulated payment resumed after redelivery." : "Simulated payment started.",
            now);
    }

    /// <summary>The simulator settled a simulated payment in flight.</summary>
    public void RecordSimulatedSuccess(string paymentIntentId, DateTime now)
    {
        RequireSimulatedInFlight(nameof(RecordSimulatedSuccess));
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new DomainException($"Payment {Id} cannot be settled without the provider's payment id.");
        }

        var previous = Status;
        Status = PaymentStatus.Success;
        PaymentIntentId = paymentIntentId;
        ErrorMessage = null;
        ProcessedAt = now;
        UpdatedAt = now;

        Record(PaymentEventKind.Transition, previous, Status, "Simulated payment settled.", now);
    }

    /// <summary>The simulator declined a simulated payment in flight. That ends it: nobody can retry a simulated card.</summary>
    public void RecordSimulatedFailure(string reason, DateTime now)
    {
        RequireSimulatedInFlight(nameof(RecordSimulatedFailure));

        var previous = Status;
        Status = PaymentStatus.Failed;
        ErrorMessage = reason;
        ProcessedAt = now;
        UpdatedAt = now;

        Record(PaymentEventKind.Transition, previous, Status, $"Simulated payment declined: {reason}", now);
    }

    /// <summary>
    /// What an offline settlement writes into <see cref="PaymentIntentId"/> in front of the operator's own reference,
    /// so a bank-transfer reference can never be mistaken for — or collide with — a Stripe intent id, in this service
    /// or in the order it ends up on.
    /// </summary>
    public const string OfflineReferencePrefix = "offline:";

    /// <summary>The stored form of an operator's offline reference. Built here so a caller's uniqueness pre-check and
    /// <see cref="SettleOffline"/> cannot disagree about the string the unique index sees.</summary>
    public static string OfflineReference(string reference) => OfflineReferencePrefix + reference.Trim();

    /// <summary>
    /// The money arrived outside the system — a bank transfer, cash at the counter — and an operator is recording that
    /// fact (Admin panel S10, decision Q6a). The payment goes straight to <see cref="PaymentStatus.Success"/> as a
    /// <see cref="PaymentMethodType.Mock"/> payment, and the <c>PaymentSuccessEvent</c> that follows reaches
    /// <c>Order.MarkAsPaid</c> by the one path that already exists.
    /// <para><b>Only a Pending payment, and that restriction is load-bearing rather than cautious.</b> Every method
    /// that records an intent — <see cref="StartStripePayment"/>, <see cref="StartSimulated"/> — leaves Pending as it
    /// does so, and nothing ever returns to it. So Pending means no attempt is in flight at a provider, and settling
    /// offline cannot strand a Stripe intent the customer could still pay. The <see cref="PaymentIntentId"/> check
    /// below states that invariant rather than trusting it.</para>
    /// </summary>
    public void SettleOffline(string reference, DateTime now)
    {
        if (Status != PaymentStatus.Pending || !string.IsNullOrEmpty(PaymentIntentId))
        {
            throw Refused(nameof(SettleOffline));
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainException($"Payment {Id} cannot be settled offline without a reference.");
        }

        var previous = Status;
        PaymentMethod = PaymentMethodType.Mock;
        PaymentIntentId = OfflineReference(reference);
        Status = PaymentStatus.Success;
        ErrorMessage = null;
        ProcessedAt = now;
        UpdatedAt = now;

        // The reference is on the timeline as well as in PaymentIntentId, because this row is the only place an
        // operator's evidence is legible as evidence rather than as an intent id.
        Record(
            PaymentEventKind.Transition,
            previous,
            Status,
            $"Recorded as paid outside the system; operator reference '{reference.Trim()}'.",
            now);
    }

    /// <summary>
    /// The customer started paying at Stripe (<c>/create-intent</c>). Only a Pending Stripe payment with no intent: one
    /// payment, one intent (S2, D4).
    /// </summary>
    public void StartStripePayment(string paymentIntentId, string stripeCustomerId, string stripeStatus, DateTime now)
    {
        if (Status != PaymentStatus.Pending
            || PaymentMethod != PaymentMethodType.Stripe
            || !string.IsNullOrEmpty(PaymentIntentId))
        {
            throw Refused(nameof(StartStripePayment));
        }

        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new DomainException($"Payment {Id} cannot start without a Stripe intent.");
        }

        var previous = Status;
        StripeCustomerId = stripeCustomerId;
        PaymentIntentId = paymentIntentId;
        StripeStatus = stripeStatus;
        Status = PaymentStatus.Processing;
        UpdatedAt = now;

        Record(
            PaymentEventKind.Transition,
            previous,
            Status,
            $"Stripe payment intent '{paymentIntentId}' created; Stripe reports '{stripeStatus}'.",
            now);
    }

    /// <summary>
    /// True when <paramref name="asOf"/> is no newer than the total this payment already reflects, so a revision
    /// carrying it must be ignored (frontend-contracts F-47).
    /// </summary>
    public bool AlreadyReflectsTotalAsOf(DateTime asOf) => AmountAsOf is { } current && current >= asOf;

    /// <summary>
    /// The order's total changed while it was still Pending, and this payment must charge the new one
    /// (frontend-contracts F-47). Allowed only while nothing has been captured at the old amount:
    /// <list type="bullet">
    ///   <item>a Pending payment, with no attempt at any provider yet;</item>
    ///   <item>a Processing Stripe payment whose intent the caller has <b>already</b> updated at Stripe, so the record
    ///   and the intent the customer is paying agree.</item>
    /// </list>
    /// A simulated payment in flight, or anything settled or closed, is refused: the old amount is already being, or
    /// has been, charged, and that needs a person.
    /// <para>The same amount only moves <see cref="AmountAsOf"/> and writes no timeline row, since nothing an operator
    /// would look for changed.</para>
    /// </summary>
    public void ReviseAmount(decimal amount, DateTime asOf, DateTime now)
    {
        var revisable = Status == PaymentStatus.Pending
            || (Status == PaymentStatus.Processing
                && PaymentMethod == PaymentMethodType.Stripe
                && !string.IsNullOrEmpty(PaymentIntentId));
        if (!revisable)
        {
            throw Refused(nameof(ReviseAmount));
        }

        if (amount < 0)
        {
            throw new DomainException($"A payment cannot be for a negative amount ({amount}).");
        }

        if (AlreadyReflectsTotalAsOf(asOf))
        {
            throw new DomainException(
                $"Payment {Id} already reflects the order total as of {AmountAsOf:O}; a revision as of {asOf:O} is older.");
        }

        var previousAmount = Amount;
        Amount = amount;
        AmountAsOf = asOf;
        UpdatedAt = now;

        if (previousAmount != amount)
        {
            Record(
                PaymentEventKind.Transition,
                Status,
                Status,
                $"Amount revised from {previousAmount} to {amount} {Currency} because the order's items changed.",
                now);
        }
    }

    /// <summary>
    /// Stripe captured the money (<c>payment_intent.succeeded</c>). Recorded from every state except Success and Refunded,
    /// including Cancelled and Failed: if Stripe took the money, the record must say so. A refund is never undone
    /// (Ordering audit Stage 19). Returns false when nothing changed.
    /// </summary>
    public bool RecordStripeSuccess(string stripeStatus, DateTime now, string? stripeEventId = null)
    {
        if (Status is PaymentStatus.Success or PaymentStatus.Refunded)
        {
            RecordIgnoredWebhook("payment_intent.succeeded", stripeStatus, now, stripeEventId);
            return false;
        }

        var previous = Status;
        Status = PaymentStatus.Success;
        StripeStatus = stripeStatus;
        ErrorMessage = null;
        ProcessedAt = now;
        UpdatedAt = now;

        Record(
            PaymentEventKind.Webhook,
            previous,
            Status,
            $"Stripe captured the payment; intent '{stripeStatus}'.",
            now,
            stripeEventId);
        return true;
    }

    /// <summary>
    /// A card was declined (<c>payment_intent.payment_failed</c>). Payment audit Stage 5 (H1, D3): that ends one attempt,
    /// not the payment. Stripe leaves the intent payable with another card, so the reason is recorded and the status
    /// stays as it was. Only a payment in flight; a decline delivered after the success changes nothing. Returns false
    /// when nothing changed.
    /// </summary>
    public bool RecordDeclinedAttempt(string? reason, string stripeStatus, DateTime now, string? stripeEventId = null)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
        {
            RecordIgnoredWebhook("payment_intent.payment_failed", stripeStatus, now, stripeEventId);
            return false;
        }

        StripeStatus = stripeStatus;
        ErrorMessage = reason ?? "Stripe payment attempt failed.";
        UpdatedAt = now;

        // From and To are the same on purpose: a decline ends one attempt, not the payment. The row exists precisely
        // because the status does NOT move, so a declined card leaves no other trace an operator can find.
        Record(
            PaymentEventKind.Webhook,
            Status,
            Status,
            $"Card declined, intent still payable: {ErrorMessage}",
            now,
            stripeEventId);
        return true;
    }

    /// <summary>
    /// Stripe cancelled the intent (<c>payment_intent.canceled</c>). Ordering audit Stage 21 (D17): tagged by
    /// <c>OrderCancelledConsumer</c>, the order was cancelled, so the payment is Cancelled. Untagged (the Dashboard, or
    /// Stripe), the payment failed. Nothing changes for Success, Refunded or Cancelled, and the result is then false.
    /// </summary>
    public bool RecordStripeCancellation(
        bool requestedByEShop,
        string stripeStatus,
        DateTime now,
        string? stripeEventId = null)
    {
        if (Status is PaymentStatus.Success or PaymentStatus.Refunded or PaymentStatus.Cancelled)
        {
            RecordIgnoredWebhook("payment_intent.canceled", stripeStatus, now, stripeEventId);
            return false;
        }

        var previous = Status;
        Status = requestedByEShop ? PaymentStatus.Cancelled : PaymentStatus.Failed;
        StripeStatus = stripeStatus;
        ErrorMessage = requestedByEShop
            ? "Payment intent cancelled because its order was cancelled."
            : "Stripe payment intent canceled.";
        ProcessedAt = now;
        UpdatedAt = now;

        Record(PaymentEventKind.Webhook, previous, Status, ErrorMessage, now, stripeEventId);
        return true;
    }

    /// <summary>
    /// A Stripe delivery that reached this payment and correctly changed nothing: a decline arriving after the
    /// success, a redelivered cancellation, a success for an already-refunded payment.
    /// <para>It is recorded rather than dropped because this is the one class of webhook that leaves no other
    /// evidence anywhere — the payment is untouched, and <c>ProcessedStripeWebhookEvents</c> records only the event
    /// id, not which payment it reached. Diagnosing "Stripe says it sent that, we say we never saw it" needs this
    /// row.</para>
    /// </summary>
    private void RecordIgnoredWebhook(string eventType, string stripeStatus, DateTime now, string? stripeEventId)
        => Record(
            PaymentEventKind.Webhook,
            Status,
            Status,
            $"Stripe sent {eventType} ('{stripeStatus}'); the payment is already {Status} and was not changed.",
            now,
            stripeEventId);

    /// <summary>The order was cancelled before its payment was captured. Only a payment still in flight.</summary>
    public void Cancel(string note, DateTime now)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
        {
            throw Refused(nameof(Cancel));
        }

        var previous = Status;
        Status = PaymentStatus.Cancelled;
        ErrorMessage = note;
        ProcessedAt = now;
        UpdatedAt = now;

        Record(PaymentEventKind.Transition, previous, Status, note, now);
    }

    /// <summary>
    /// What Stripe says about the intent. An observation, not a transition: allowed in every state.
    /// <para>Deliberately writes no timeline row. It reports the intent's state, not the payment's, and every caller
    /// follows it within the same save with a real transition — a cancel, a refund or an exception — whose row is the
    /// one that says what happened. <see cref="StripeStatus"/> already carries the latest observation.</para>
    /// </summary>
    public void ObserveStripeStatus(string stripeStatus, DateTime now)
    {
        StripeStatus = stripeStatus;
        UpdatedAt = now;
    }

    /// <summary>
    /// The money went back to the customer. Refused for Refunded (a payment is refunded once) and Cancelled (it was
    /// never captured). Allowed from Success. Also allowed from a payment the record still shows in flight or Failed,
    /// when the provider has confirmed a capture the record had not caught up with: the automatic refund of a cancelled
    /// order (Ordering audit Stage 19, Payment audit S5).
    /// </summary>
    public void MarkRefunded(DateTime now)
    {
        if (Status is PaymentStatus.Refunded or PaymentStatus.Cancelled)
        {
            throw Refused(nameof(MarkRefunded));
        }

        var previous = Status;
        Status = PaymentStatus.Refunded;
        ProcessedAt = now;
        UpdatedAt = now;

        Record(PaymentEventKind.Transition, previous, Status, $"Refunded {Amount} {Currency} in full.", now);
    }

    /// <summary>
    /// Why a refunded payment was refunded: the cancellation of its order, or an admin's own reason.
    /// <para>It gets its own timeline row, with From and To both Refunded. Folding the note into
    /// <see cref="MarkRefunded"/>'s row was not possible — both callers apply it afterwards, once the provider has
    /// confirmed the refund — and dropping it would leave the reason readable only as <see cref="ErrorMessage"/> on a
    /// successful payment, which is exactly the reading S10 refused to create for offline settlements.</para>
    /// </summary>
    public void AnnotateRefund(string note)
    {
        if (Status != PaymentStatus.Refunded)
        {
            throw Refused(nameof(AnnotateRefund));
        }

        ErrorMessage = note;

        Record(PaymentEventKind.Transition, Status, Status, $"Refund reason recorded: {note}", DateTime.UtcNow);
    }

    private void RequireSimulatedInFlight(string action)
    {
        if (Status != PaymentStatus.Processing || PaymentMethod != PaymentMethodType.Mock)
        {
            throw Refused(action);
        }
    }

    private DomainException Refused(string action)
        => new($"Payment {Id} is {Status} ({PaymentMethod}); {action} is not allowed.");
}

public enum PaymentStatus
{
    Pending,
    Processing,
    Success,
    Failed,
    Refunded,

    /// <summary>
    /// The order was cancelled before the payment was captured, and any Stripe intent was cancelled
    /// with it (Ordering audit Stage 9). Final: nothing will charge this order afterwards.
    /// </summary>
    Cancelled
}

/// <summary>
/// How a payment is settled. It is stored as its name, the same strings the column always held. Payment audit Stage 7
/// (H5): it used to be free text, compared as a string in three places, and before Stage 3 a client could set it to
/// anything. The <c>NormalizePaymentMethod</c> migration maps any other stored value onto these three.
/// </summary>
public enum PaymentMethodType
{
    /// <summary>No payment was started: the record for an order cancelled before its payment was recorded.</summary>
    None,

    /// <summary>The simulator (<c>MockPaymentProcessor</c>), with Stripe off, or an admin settling a payment (D2).</summary>
    Mock,

    /// <summary>The customer pays at Stripe through <c>/create-intent</c>.</summary>
    Stripe
}
