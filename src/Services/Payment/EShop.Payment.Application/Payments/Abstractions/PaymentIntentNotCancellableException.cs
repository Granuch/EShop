namespace EShop.Payment.Application.Payments.Abstractions;

/// <summary>
/// Stripe refused to cancel a payment intent because of its state — typically because it has
/// already succeeded and the customer has been charged. See
/// <see cref="IStripePaymentService.CancelPaymentIntentAsync"/>.
/// </summary>
public sealed class PaymentIntentNotCancellableException : Exception
{
    public PaymentIntentNotCancellableException(string paymentIntentId, string stripeMessage, Exception? innerException = null)
        : base($"Payment intent {paymentIntentId} cannot be cancelled: {stripeMessage}", innerException)
    {
        PaymentIntentId = paymentIntentId;
        StripeMessage = stripeMessage;
    }

    public string PaymentIntentId { get; }

    /// <summary>Stripe's own explanation, e.g. "This PaymentIntent's status is succeeded".</summary>
    public string StripeMessage { get; }
}
