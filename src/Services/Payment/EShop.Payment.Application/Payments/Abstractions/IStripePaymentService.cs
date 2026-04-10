namespace EShop.Payment.Application.Payments.Abstractions;

public interface IStripePaymentService
{
    Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        StripePaymentIntentRequest request,
        CancellationToken cancellationToken = default);

    Task<StripeRefundResult> CreateRefundAsync(string paymentIntentId, decimal amount, string currency, CancellationToken cancellationToken = default);

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

public sealed record StripeWebhookEvent(
    string Id,
    string Type,
    string PaymentIntentId,
    string Status,
    string? FailureMessage,
    bool IsSupportedPaymentIntentEvent);
