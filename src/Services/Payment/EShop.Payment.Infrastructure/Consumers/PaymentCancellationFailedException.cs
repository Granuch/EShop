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
/// refusal for most of an hour. Ordering's <c>InvalidCheckoutEventException</c> uses the same device.
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
