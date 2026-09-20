using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Stripe;

namespace EShop.Payment.Infrastructure.Services;

/// <summary>
/// Payment audit Stage 4 (M10). Kept apart from <see cref="StripePaymentService"/>: checking a webhook needs only the
/// webhook secret, never the API key or a Stripe client. That is also what lets the HTTP tests check real signatures
/// while every call to Stripe is mocked.
/// </summary>
public sealed class StripeWebhookEventParser : IStripeWebhookEventParser
{
    /// <summary>
    /// Stripe.net's own default: a delivery signed more than five minutes ago is refused, so one captured on the
    /// wire cannot be replayed later.
    /// </summary>
    private const long SignatureToleranceSeconds = 300;

    private readonly StripeSettings _settings;

    public StripeWebhookEventParser(IOptions<StripeSettings> settings)
    {
        _settings = settings.Value;
    }

    public StripeWebhookEvent Parse(string payload, string signatureHeader)
    {
        // The signature is checked on its own, before parsing, so a failure is classified by which step threw rather
        // than by the text of Stripe's message.
        if (!_settings.SkipWebhookSignatureVerification)
        {
            try
            {
                EventUtility.ValidateSignature(payload, signatureHeader, _settings.WebhookSecret, SignatureToleranceSeconds);
            }
            catch (Exception ex)
            {
                // Admin panel S11 widened this from StripeException. Any failure to validate a signature is a refusal,
                // never a server error — and that is now load-bearing rather than tidy: the capture table's whole
                // safety argument is "a row only exists here because Parse accepted the delivery", so a validation
                // failure escaping as some other exception would reach the endpoint's internal-failure branch and
                // capture an UNVERIFIED payload for later trusted replay.
                throw new StripeWebhookRejectedException(StripeWebhookRejection.InvalidSignature, ex);
            }
        }

        return ReadEvent(payload);
    }

    public StripeWebhookEvent ParseTrusted(string payload) => ReadEvent(payload);

    private static StripeWebhookEvent ReadEvent(string payload)
    {
        Event? stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex)
        {
            // Every exception here is the payload's: ParseEvent only deserializes the bytes it is given. It is not only
            // JSON errors either. Stripe.net's EventConverter throws NullReferenceException for an object with no
            // event fields, such as {}.
            throw new StripeWebhookRejectedException(StripeWebhookRejection.MalformedPayload, ex);
        }

        // Valid JSON that is not an event ({}, null) parses without error. Stripe always sends both.
        if (stripeEvent is null || string.IsNullOrEmpty(stripeEvent.Id) || string.IsNullOrEmpty(stripeEvent.Type))
        {
            throw new StripeWebhookRejectedException(StripeWebhookRejection.MalformedPayload);
        }

        if (stripeEvent.Data?.Object is not PaymentIntent paymentIntent)
        {
            return new StripeWebhookEvent(
                stripeEvent.Id,
                stripeEvent.Type,
                string.Empty,
                string.Empty,
                null,
                false);
        }

        return new StripeWebhookEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            paymentIntent.Id,
            paymentIntent.Status ?? string.Empty,
            paymentIntent.LastPaymentError?.Message,
            stripeEvent.Type is "payment_intent.succeeded"
                or "payment_intent.payment_failed"
                or "payment_intent.canceled",
            StripePaymentService.IsCancelRequestedByEShop(paymentIntent));
    }
}
