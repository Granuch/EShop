namespace EShop.Payment.Infrastructure.Consumers;

/// <summary>
/// An order's total changed before <c>OrderCreatedConsumer</c> had recorded its payment. The two events travel in
/// separate queues, so this can happen, briefly. Thrown so the message is retried (the policies retry an
/// <see cref="InvalidOperationException"/>), by which time the payment exists and the revision applies.
/// </summary>
public sealed class PaymentNotRecordedYetException : InvalidOperationException
{
    public PaymentNotRecordedYetException(Guid orderId)
        : base($"No payment is recorded yet for order {orderId}; the total change will be retried.")
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; }
}
