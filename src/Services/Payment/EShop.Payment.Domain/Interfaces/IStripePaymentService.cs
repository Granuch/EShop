namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// Payment's calls to Stripe. Payment audit Stage 12 (D18) moved this interface, its types, the other Stripe
/// interfaces and their two exceptions here from <c>Application/Payments/Abstractions</c>. They now sit beside
/// <see cref="IPaymentProcessor"/>, where the repo's convention puts an interface that Application calls and
/// Infrastructure implements.
/// </summary>
public interface IStripePaymentService
{
    Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        StripePaymentIntentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds an intent. A repeat within Stripe's idempotency window returns the first refund. A charge
    /// Stripe reports as already refunded counts as success, with
    /// <see cref="StripeRefundResult.AlreadyRefunded"/> set: the money is back with the customer, which
    /// is all a caller asked for (Ordering audit Stage 19).
    /// </summary>
    Task<StripeRefundResult> CreateRefundAsync(string paymentIntentId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>
    /// The intent's status at Stripe right now — <c>succeeded</c>, <c>processing</c>,
    /// <c>requires_payment_method</c>, <c>canceled</c> and so on.
    /// </summary>
    Task<string> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an existing intent back from Stripe, client secret included (frontend-contracts F-52). This is how
    /// <c>/create-intent</c> resumes a payment whose intent is already recorded: the secret is never stored here, so a
    /// customer who lost it — a reload, a second tab, a response that never arrived — gets it from Stripe again. A
    /// failure a retry can fix is <see cref="PaymentProviderUnavailableException"/>.
    /// </summary>
    Task<StripePaymentIntentResult> GetPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an intent that has not been captured. An intent Stripe reports as already canceled
    /// counts as success. The intent is first tagged as cancelled at EShop's request, so the
    /// <c>payment_intent.canceled</c> webhook that follows reads <see cref="StripeWebhookEvent.CancelRequestedByEShop"/>
    /// (Ordering audit Stage 21, D17).
    /// </summary>
    /// <exception cref="PaymentIntentNotCancellableException">
    /// Stripe refused because of the intent's state — above all, it has already succeeded, so the
    /// customer has been charged. Deterministic: retrying cannot change the answer.
    /// </exception>
    /// <remarks>Any other failure propagates unchanged, so a transient one is retried.</remarks>
    Task<StripePaymentIntentCancelResult> CancelPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the amount of an intent the customer has not paid yet, so an open payment form charges the order's
    /// new total (frontend-contracts F-47). The client secret does not change. Returns the intent's status.
    /// </summary>
    /// <exception cref="PaymentIntentNotUpdatableException">
    /// Stripe refused because of the intent's state: it is processing, has succeeded, or was cancelled, so the old
    /// amount stands. Deterministic: retrying cannot change the answer.
    /// </exception>
    /// <remarks>A failure to reach Stripe is <see cref="PaymentProviderUnavailableException"/> and is retried.</remarks>
    Task<string> UpdatePaymentIntentAmountAsync(
        string paymentIntentId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);
}

public sealed record StripePaymentIntentRequest(
    Guid PaymentId,
    Guid OrderId,
    string UserId,
    string StripeCustomerId,
    decimal Amount,
    string Currency);

public sealed record StripePaymentIntentResult(
    string PaymentIntentId,
    string ClientSecret,
    string Status);

/// <param name="AlreadyRefunded">
/// Stripe refused because the charge had been refunded already; <see cref="RefundId"/> is then empty.
/// </param>
public sealed record StripeRefundResult(
    string RefundId,
    string Status,
    bool AlreadyRefunded = false);

public sealed record StripePaymentIntentCancelResult(
    string PaymentIntentId,
    string Status);

/// <param name="CancelRequestedByEShop">
/// The intent carries the tag <see cref="IStripePaymentService.CancelPaymentIntentAsync"/> sets before it
/// cancels. So a <c>payment_intent.canceled</c> event is the cancellation of a cancelled order, not a
/// payment that failed.
/// </param>
public sealed record StripeWebhookEvent(
    string Id,
    string Type,
    string PaymentIntentId,
    string Status,
    string? FailureMessage,
    bool IsSupportedPaymentIntentEvent,
    bool CancelRequestedByEShop = false);
