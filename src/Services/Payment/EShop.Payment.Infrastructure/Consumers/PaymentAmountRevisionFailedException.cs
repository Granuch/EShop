namespace EShop.Payment.Infrastructure.Consumers;

/// <summary>
/// An order's total changed, but its payment can no longer follow: the old amount has been, or is being, charged
/// (frontend-contracts F-47). Ordering will also refuse the success, because the amounts differ, so the order needs a
/// person — a refund and a new payment, or a correction of the order.
///
/// <para>
/// Derives from <see cref="ArgumentException"/> for the same reason as <see cref="PaymentCancellationFailedException"/>:
/// the retry and delayed-redelivery policies ignore it, so the message goes to
/// <c>payment_order_total_changed_error</c> with its payload at once instead of retrying a deterministic refusal.
/// </para>
/// </summary>
public sealed class PaymentAmountRevisionFailedException : ArgumentException
{
    public PaymentAmountRevisionFailedException(Guid orderId, string reason, Exception? innerException = null)
        : base($"The payment for order {orderId} could not be revised to the order's new total: {reason}", innerException)
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; }
}
