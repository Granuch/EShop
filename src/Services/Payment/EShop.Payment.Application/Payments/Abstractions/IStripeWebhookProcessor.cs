namespace EShop.Payment.Application.Payments.Abstractions;

public interface IStripeWebhookProcessor
{
    Task<StripeWebhookProcessResult> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default);
}

public sealed record StripeWebhookProcessResult(
    bool IsDuplicate,
    bool PaymentFound,
    string EventId,
    string EventType);
