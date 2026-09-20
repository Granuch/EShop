namespace EShop.Payment.Domain.Interfaces;

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

    /// <summary>
    /// Parses a payload this service has <b>already verified and stored</b>, without checking a signature (Admin
    /// panel S11, endpoint #67).
    ///
    /// <para>
    /// <b>Why replay cannot re-verify.</b> Stripe's signature carries the instant it was made and
    /// <see cref="Parse"/> refuses anything older than 300 seconds, so re-checking a capture is not a stricter
    /// option — it is an impossible one, and an operator replaying yesterday's incident would be told the delivery is
    /// forged.
    /// </para>
    /// <para>
    /// <b>Why it is safe anyway.</b> A payload only reaches the capture table after <see cref="Parse"/> accepted it:
    /// a delivery refused for its signature throws <see cref="StripeWebhookRejectedException"/> and is answered 400
    /// without being captured. So "captured" already means "verified", and this method re-reads bytes we vouched for
    /// rather than bytes a caller supplied. It must therefore never be reachable from a request body.
    /// </para>
    /// </summary>
    /// <exception cref="StripeWebhookRejectedException">The stored payload is not a Stripe event.</exception>
    StripeWebhookEvent ParseTrusted(string payload);
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
