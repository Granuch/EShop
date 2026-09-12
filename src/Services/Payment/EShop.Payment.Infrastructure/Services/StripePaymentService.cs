using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Stripe;

namespace EShop.Payment.Infrastructure.Services;

public sealed class StripePaymentService : IStripePaymentService
{
    private static readonly HashSet<string> ZeroDecimalCurrencies =
    [
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf"
    ];

    private readonly StripeSettings _settings;

    public StripePaymentService(IOptions<StripeSettings> settings)
    {
        _settings = settings.Value;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        StripePaymentIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        var currency = NormalizeCurrency(request.Currency);
        var amountMinor = ConvertToMinorUnits(request.Amount, currency);

        var paymentIntentService = new PaymentIntentService();
        var intent = await paymentIntentService.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = amountMinor,
            Currency = currency,
            Customer = request.StripeCustomerId,
            ConfirmationMethod = "automatic",
            Confirm = false,
            PaymentMethodTypes = ["card"],
            Metadata = new Dictionary<string, string>
            {
                ["paymentId"] = request.PaymentId.ToString(),
                ["orderId"] = request.OrderId.ToString(),
                ["userId"] = request.UserId
            }
        }, cancellationToken: cancellationToken);

        return new StripePaymentIntentResult(
            intent.Id,
            intent.ClientSecret ?? string.Empty,
            intent.Status ?? string.Empty);
    }

    public async Task<StripeRefundResult> CreateRefundAsync(
        string paymentIntentId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var amountMinor = ConvertToMinorUnits(amount, normalizedCurrency);

        // Refunds are full-only (Ordering audit Stage 11), so an intent is refunded at most once and its id
        // names the refund. A repeat of the same request within Stripe's idempotency window (24 hours) —
        // a double submit, or a retry after a lost response — returns the first refund instead of a second.
        var refundService = new RefundService();
        var refund = await refundService.CreateAsync(
            new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Amount = amountMinor
            },
            new RequestOptions { IdempotencyKey = $"refund-{paymentIntentId}" },
            cancellationToken);

        return new StripeRefundResult(refund.Id, refund.Status ?? string.Empty);
    }

    public async Task<StripePaymentIntentCancelResult> CancelPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        var paymentIntentService = new PaymentIntentService();

        try
        {
            var intent = await paymentIntentService.CancelAsync(
                paymentIntentId,
                new PaymentIntentCancelOptions { CancellationReason = "requested_by_customer" },
                cancellationToken: cancellationToken);

            return new StripePaymentIntentCancelResult(intent.Id, intent.Status ?? string.Empty);
        }
        catch (StripeException ex) when (IsAlreadyCanceled(ex))
        {
            // Cancelling twice is not a failure: the intent is in the state we asked for.
            return new StripePaymentIntentCancelResult(paymentIntentId, "canceled");
        }
        catch (StripeException ex) when (IsUnexpectedState(ex))
        {
            throw new PaymentIntentNotCancellableException(
                paymentIntentId,
                ex.StripeError?.Message ?? ex.Message,
                ex);
        }
    }

    /// <summary>
    /// Stripe's answer when an intent cannot move to the requested state — for a cancel, because it
    /// has already succeeded (or is processing, or is already canceled). A structured code, not the
    /// message text, which Stripe may reword.
    /// </summary>
    public static bool IsUnexpectedState(StripeException ex)
        => string.Equals(ex.StripeError?.Code, "payment_intent_unexpected_state", StringComparison.Ordinal);

    public static bool IsAlreadyCanceled(StripeException ex)
        => IsUnexpectedState(ex)
           && string.Equals(ex.StripeError?.PaymentIntent?.Status, "canceled", StringComparison.Ordinal);

    public StripeWebhookEvent ConstructWebhookEvent(string payload, string signatureHeader)
    {
        Event stripeEvent;

        if (_settings.SkipWebhookSignatureVerification)
        {
            stripeEvent = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }
        else
        {
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    payload,
                    signatureHeader,
                    _settings.WebhookSecret,
                    throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex) when (ex.Message.Contains("signature", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid Stripe webhook signature.", ex);
            }
            catch (StripeException ex)
            {
                throw new InvalidOperationException("Stripe webhook payload parsing failed.", ex);
            }
        }

        if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
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
                or "payment_intent.canceled");
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return "usd";
        }

        return currency.Trim().ToLowerInvariant();
    }

    private static long ConvertToMinorUnits(decimal amount, string currency)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return decimal.ToInt64(decimal.Round(amount, 0, MidpointRounding.AwayFromZero));
        }

        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }
}
