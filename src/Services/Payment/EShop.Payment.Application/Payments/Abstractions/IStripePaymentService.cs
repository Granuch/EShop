namespace EShop.Payment.Application.Payments.Abstractions;

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
    /// Cancels an intent that has not been captured. An intent Stripe reports as already canceled
    /// counts as success.
    /// </summary>
    /// <exception cref="PaymentIntentNotCancellableException">
    /// Stripe refused because of the intent's state — above all, it has already succeeded, so the
    /// customer has been charged. Deterministic: retrying cannot change the answer.
    /// </exception>
    /// <remarks>Any other failure propagates unchanged, so a transient one is retried.</remarks>
    Task<StripePaymentIntentCancelResult> CancelPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default);

    StripeWebhookEvent ConstructWebhookEvent(string payload, string signatureHeader);
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

public sealed record StripeWebhookEvent(
    string Id,
    string Type,
    string PaymentIntentId,
    string Status,
    string? FailureMessage,
    bool IsSupportedPaymentIntentEvent);
