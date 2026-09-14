namespace EShop.Payment.Infrastructure.Consumers;

/// <summary>
/// A cancelled order whose payment could not be cancelled — the customer has been, or is being,
/// charged for an order that no longer exists. This must reach a person, so it is thrown, never
/// logged and acknowledged (Ordering audit Stage 9).
///
/// <para>
/// Derives from <see cref="ArgumentException"/> on purpose: <c>AddMessaging</c>'s retry and
/// delayed-redelivery policies <c>Ignore&lt;ArgumentException&gt;</c>, so the message goes to
/// <c>payment_order_cancelled_error</c> with its payload at once instead of retrying a deterministic
/// refusal. What that saves depends on configuration: the three immediate retries (about 45 s by
/// default), plus the minutes-long delayed-redelivery tier only where
/// <c>RabbitMQ:UseDelayedExchangePlugin</c> is enabled. Ordering's <c>InvalidCheckoutEventException</c>
/// uses the same device.
/// </para>
/// <para>
/// The same error queue also receives any other failure once its retries run out (a Stripe or
/// database outage), so a message there is not by itself proof that money was taken: check the
/// payment's current state before refunding.
/// </para>
/// </summary>
public sealed class PaymentCancellationFailedException : ArgumentException
{
    public PaymentCancellationFailedException(Guid orderId, string reason, Exception? innerException = null)
        : base($"The payment for cancelled order {orderId} could not be cancelled: {reason}", innerException)
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; }
}
