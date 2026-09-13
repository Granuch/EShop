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

        return new PaymentTransaction
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
    }

    /// <summary>
    /// The record left when a cancellation overtakes <c>OrderCreatedEvent</c> (Ordering audit Stage 9). It is final, so the
    /// late event never charges the order.
    /// </summary>
    public static PaymentTransaction RecordCancelledBeforeCreation(Guid orderId, string userId, string note, DateTime now)
        => new()
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

        PaymentMethod = PaymentMethodType.Mock;
        Status = PaymentStatus.Processing;
        UpdatedAt = now;
    }

    /// <summary>The simulator settled a simulated payment in flight.</summary>
    public void RecordSimulatedSuccess(string paymentIntentId, DateTime now)
    {
        RequireSimulatedInFlight(nameof(RecordSimulatedSuccess));
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new DomainException($"Payment {Id} cannot be settled without the provider's payment id.");
        }

        Status = PaymentStatus.Success;
        PaymentIntentId = paymentIntentId;
        ErrorMessage = null;
        ProcessedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The simulator declined a simulated payment in flight. That ends it: nobody can retry a simulated card.</summary>
    public void RecordSimulatedFailure(string reason, DateTime now)
    {
        RequireSimulatedInFlight(nameof(RecordSimulatedFailure));

        Status = PaymentStatus.Failed;
        ErrorMessage = reason;
        ProcessedAt = now;
        UpdatedAt = now;
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

        StripeCustomerId = stripeCustomerId;
        PaymentIntentId = paymentIntentId;
        StripeStatus = stripeStatus;
        Status = PaymentStatus.Processing;
        UpdatedAt = now;
    }

    /// <summary>
    /// Stripe captured the money (<c>payment_intent.succeeded</c>). Recorded from every state except Success and Refunded,
    /// including Cancelled and Failed: if Stripe took the money, the record must say so. A refund is never undone
    /// (Ordering audit Stage 19). Returns false when nothing changed.
    /// </summary>
    public bool RecordStripeSuccess(string stripeStatus, DateTime now)
    {
        if (Status is PaymentStatus.Success or PaymentStatus.Refunded)
        {
            return false;
        }

        Status = PaymentStatus.Success;
        StripeStatus = stripeStatus;
        ErrorMessage = null;
        ProcessedAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// A card was declined (<c>payment_intent.payment_failed</c>). Payment audit Stage 5 (H1, D3): that ends one attempt,
    /// not the payment. Stripe leaves the intent payable with another card, so the reason is recorded and the status
    /// stays as it was. Only a payment in flight; a decline delivered after the success changes nothing. Returns false
    /// when nothing changed.
    /// </summary>
    public bool RecordDeclinedAttempt(string? reason, string stripeStatus, DateTime now)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
        {
            return false;
        }

        StripeStatus = stripeStatus;
        ErrorMessage = reason ?? "Stripe payment attempt failed.";
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Stripe cancelled the intent (<c>payment_intent.canceled</c>). Ordering audit Stage 21 (D17): tagged by
    /// <c>OrderCancelledConsumer</c>, the order was cancelled, so the payment is Cancelled. Untagged (the Dashboard, or
    /// Stripe), the payment failed. Nothing changes for Success, Refunded or Cancelled, and the result is then false.
    /// </summary>
    public bool RecordStripeCancellation(bool requestedByEShop, string stripeStatus, DateTime now)
    {
        if (Status is PaymentStatus.Success or PaymentStatus.Refunded or PaymentStatus.Cancelled)
        {
            return false;
        }

        Status = requestedByEShop ? PaymentStatus.Cancelled : PaymentStatus.Failed;
        StripeStatus = stripeStatus;
        ErrorMessage = requestedByEShop
            ? "Payment intent cancelled because its order was cancelled."
            : "Stripe payment intent canceled.";
        ProcessedAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The order was cancelled before its payment was captured. Only a payment still in flight.</summary>
    public void Cancel(string note, DateTime now)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
        {
            throw Refused(nameof(Cancel));
        }

        Status = PaymentStatus.Cancelled;
        ErrorMessage = note;
        ProcessedAt = now;
        UpdatedAt = now;
    }

    /// <summary>What Stripe says about the intent. An observation, not a transition: allowed in every state.</summary>
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

        Status = PaymentStatus.Refunded;
        ProcessedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Why a refunded payment was refunded: the cancellation of its order.</summary>
    public void AnnotateRefund(string note)
    {
        if (Status != PaymentStatus.Refunded)
        {
            throw Refused(nameof(AnnotateRefund));
        }

        ErrorMessage = note;
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
