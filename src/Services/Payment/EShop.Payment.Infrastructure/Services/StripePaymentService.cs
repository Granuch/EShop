using EShop.Payment.Domain.Interfaces;
using Stripe;

namespace EShop.Payment.Infrastructure.Services;

public sealed class StripePaymentService : IStripePaymentService
{
    private static readonly HashSet<string> ZeroDecimalCurrencies =
    [
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf"
    ];

    /// <summary>
    /// Intent metadata key set to <c>"true"</c> before this service cancels an intent (Ordering audit Stage
    /// 21, D17). Read back from webhooks as <see cref="StripeWebhookEvent.CancelRequestedByEShop"/>.
    /// </summary>
    public const string CancelRequestedMetadataKey = "eshop_cancel_requested";

    private readonly IStripeClient _client;

    /// <summary>
    /// Payment audit Stage 9 (M4). Every call goes through the injected client. This constructor used to write the key
    /// into Stripe.net's process-wide <c>StripeConfiguration</c>, and every call used the process-wide client.
    /// </summary>
    public StripePaymentService(IStripeClient client)
    {
        _client = client;
    }

    public async Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        StripePaymentIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        var currency = NormalizeCurrency(request.Currency);
        var amountMinor = ConvertToMinorUnits(request.Amount, currency);

        var paymentIntentService = new PaymentIntentService(_client);
        PaymentIntent intent;
        try
        {
            // Payment audit Stage 6 (M1). One payment has one intent. The handler records the intent after Stripe
            // answers (S6b), and that save can be lost. The key makes the retry get the same intent back from Stripe
            // (for 24 hours) instead of opening a second one nobody can pay.
            intent = await paymentIntentService.CreateAsync(
                new PaymentIntentCreateOptions
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
                },
                new RequestOptions { IdempotencyKey = IntentIdempotencyKey(request.PaymentId, amountMinor) },
                cancellationToken);
        }
        catch (Exception ex) when (StripeErrors.IsTransient(ex, cancellationToken))
        {
            throw new PaymentProviderUnavailableException("create payment intent", ex);
        }

        return new StripePaymentIntentResult(
            intent.Id,
            intent.ClientSecret ?? string.Empty,
            intent.Status ?? string.Empty);
    }

    /// <summary>
    /// The idempotency key for a payment's intent: one payment, one intent — for one amount.
    /// <para>The amount is part of the key since frontend-contracts F-47. A payment's amount can now change after an
    /// attempt to create its intent (the order's items changed), and Stripe refuses a key reused with different
    /// parameters for 24 hours. Keyed by payment alone, a create-intent that lost its save to the amount revision
    /// would have cancelled its intent, and every retry at the new amount would then have been refused for a day. A
    /// retry at the same amount still gets the same intent back.</para>
    /// </summary>
    public static string IntentIdempotencyKey(Guid paymentId, long amountMinor) => $"payment-intent-{paymentId}-{amountMinor}";

    public async Task<string> UpdatePaymentIntentAmountAsync(
        string paymentIntentId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var amountMinor = ConvertToMinorUnits(amount, normalizedCurrency);

        try
        {
            // No idempotency key of ours: setting an amount is idempotent by nature, and Stripe would refuse a key of
            // ours reused for a later revision to a different amount. Stripe.net sends a random one per request.
            var intent = await new PaymentIntentService(_client).UpdateAsync(
                paymentIntentId,
                new PaymentIntentUpdateOptions { Amount = amountMinor, Currency = normalizedCurrency },
                cancellationToken: cancellationToken);

            return intent.Status ?? string.Empty;
        }
        catch (StripeException ex) when (IsUnexpectedState(ex))
        {
            throw new PaymentIntentNotUpdatableException(
                paymentIntentId,
                ex.StripeError?.Message ?? ex.Message,
                ex);
        }
        catch (Exception ex) when (StripeErrors.IsTransient(ex, cancellationToken))
        {
            throw new PaymentProviderUnavailableException("update payment intent amount", ex);
        }
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
        var refundService = new RefundService(_client);

        try
        {
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
        catch (StripeException ex) when (IsAlreadyRefunded(ex))
        {
            // Ordering audit Stage 19. Past the idempotency window Stripe no longer replays the first
            // refund; it refuses a second one instead. The usual way here is a refund whose database
            // commit was lost, retried a day later. The money is already back, so that is success: the
            // caller records the refund rather than dead-lettering a payment that is settled.
            // StripeSandboxTests checks this error code against the real Stripe sandbox.
            return new StripeRefundResult(string.Empty, "succeeded", AlreadyRefunded: true);
        }
    }

    public async Task<string> GetPaymentIntentStatusAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        var intent = await new PaymentIntentService(_client).GetAsync(paymentIntentId, cancellationToken: cancellationToken);
        return intent.Status ?? string.Empty;
    }

    public async Task<StripePaymentIntentResult> GetPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        PaymentIntent intent;
        try
        {
            // A read with the secret key returns the intent's client secret; StripeSandboxTests pins that against the
            // real sandbox, because a mock would only repeat our belief about it.
            intent = await new PaymentIntentService(_client).GetAsync(paymentIntentId, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (StripeErrors.IsTransient(ex, cancellationToken))
        {
            throw new PaymentProviderUnavailableException("read payment intent", ex);
        }

        return new StripePaymentIntentResult(
            intent.Id,
            intent.ClientSecret ?? string.Empty,
            intent.Status ?? string.Empty);
    }

    /// <summary>
    /// Stripe's answer to refunding a charge that has no refundable amount left. A structured code, not
    /// the message text.
    /// </summary>
    public static bool IsAlreadyRefunded(StripeException ex)
        => string.Equals(ex.StripeError?.Code, "charge_already_refunded", StringComparison.Ordinal);

    public async Task<StripePaymentIntentCancelResult> CancelPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        var paymentIntentService = new PaymentIntentService(_client);

        // Ordering audit Stage 21 (D17). Tag the intent before cancelling it. Stripe's own
        // payment_intent.canceled webhook carries the intent as it was when cancelled, tag included, which
        // is how the webhook tells a cancelled order from a payment cancelled in the Dashboard or by
        // Stripe. Without it the webhook recorded Failed and sent PaymentFailedEvent — a "payment failed"
        // email to a customer who had cancelled — whenever it committed before OrderCancelledConsumer.
        // Stripe merges metadata, so the intent's orderId and paymentId stay.
        try
        {
            await paymentIntentService.UpdateAsync(
                paymentIntentId,
                new PaymentIntentUpdateOptions
                {
                    Metadata = new Dictionary<string, string> { [CancelRequestedMetadataKey] = "true" }
                },
                cancellationToken: cancellationToken);
        }
        catch (StripeException ex) when (IsUnexpectedState(ex))
        {
            // A safety net, not an observed path: the Stripe sandbox accepts this metadata update on canceled
            // and succeeded intents alike (checked in Stage 21 — disabling this catch changed no test). Should
            // Stripe ever refuse it, the cancel below still classifies the intent exactly as before the tag.
        }

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

    /// <summary>Read by <see cref="StripeWebhookEventParser"/> from the intent a webhook carries.</summary>
    public static bool IsCancelRequestedByEShop(PaymentIntent paymentIntent)
        => paymentIntent.Metadata is not null
           && paymentIntent.Metadata.TryGetValue(CancelRequestedMetadataKey, out var tag)
           && string.Equals(tag, "true", StringComparison.Ordinal);

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
