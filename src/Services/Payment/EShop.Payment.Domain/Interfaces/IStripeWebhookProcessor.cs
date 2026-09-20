namespace EShop.Payment.Domain.Interfaces;

public interface IStripeWebhookProcessor
{
    /// <summary>The live path: verify the delivery's signature, parse it, apply it.</summary>
    Task<StripeWebhookProcessResult> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an already-parsed delivery (Admin panel S11). Split out of <see cref="ProcessAsync"/> so a replay of a
    /// captured payload runs the <b>same</b> code — including the <c>ProcessedStripeWebhookEvents</c> duplicate check
    /// that makes a replay idempotent — rather than a second implementation of it that could drift.
    /// </summary>
    Task<StripeWebhookProcessResult> ApplyAsync(StripeWebhookEvent stripeEvent, CancellationToken cancellationToken = default);
}

public sealed record StripeWebhookProcessResult(
    bool IsDuplicate,
    bool PaymentFound,
    string EventId,
    string EventType);
