namespace EShop.Payment.Application.Payments.Abstractions;

/// <summary>
/// Turns one Stripe webhook delivery into a <see cref="StripeWebhookEvent"/>, checking its signature unless
/// <c>Stripe:SkipWebhookSignatureVerification</c> is set (Payment audit Stage 4).
/// </summary>
public interface IStripeWebhookEventParser
{
    /// <exception cref="StripeWebhookRejectedException">
    /// The signature does not match the payload, or the payload is not a Stripe event. The sender's fault:
    /// redelivering the same bytes cannot succeed.
    /// </exception>
    StripeWebhookEvent Parse(string payload, string signatureHeader);
}

public enum StripeWebhookRejection
{
    InvalidSignature,
    MalformedPayload
}

/// <summary>
/// A webhook delivery refused for what it contains (Payment audit Stage 4, M10). The endpoint used to recognise
/// these by the text of an <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/> message,
/// and a payload that failed to parse in bypass mode matched neither, so it was answered 500 and Stripe kept
/// redelivering it.
/// </summary>
public sealed class StripeWebhookRejectedException : Exception
{
    public StripeWebhookRejectedException(StripeWebhookRejection reason, Exception? innerException = null)
        : base(reason == StripeWebhookRejection.InvalidSignature
            ? "Stripe webhook signature does not match the payload."
            : "Stripe webhook payload is not a Stripe event.", innerException)
    {
        Reason = reason;
    }

    public StripeWebhookRejection Reason { get; }
}
