namespace EShop.Payment.Application.Payments.Abstractions;

public interface IStripePaymentService
{
    Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        StripePaymentIntentRequest request,
        CancellationToken cancellationToken = default);

    Task<StripeRefundResult> CreateRefundAsync(string paymentIntentId, decimal amount, string currency, CancellationToken cancellationToken = default);

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

public sealed record StripeRefundResult(
    string RefundId,
    string Status);

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
