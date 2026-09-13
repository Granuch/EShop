namespace EShop.Payment.Domain.Interfaces;

public interface IStripeWebhookProcessor
{
    Task<StripeWebhookProcessResult> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default);
}

public sealed record StripeWebhookProcessResult(
    bool IsDuplicate,
    bool PaymentFound,
    string EventId,
    string EventType);
