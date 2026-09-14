namespace EShop.Payment.Application.Payments.Refunds;

/// <summary>
/// The payment provider answered a refund request and refused it — as opposed to not answering at all,
/// which propagates as the provider's own exception. Deterministic, so retrying will not help.
/// </summary>
public sealed class PaymentRefundRefusedException : Exception
{
    public PaymentRefundRefusedException(Guid paymentId, string reason)
        : base($"The refund of payment {paymentId} was refused: {reason}")
    {
        PaymentId = paymentId;
        Reason = reason;
    }

    public Guid PaymentId { get; }

    public string Reason { get; }
}
