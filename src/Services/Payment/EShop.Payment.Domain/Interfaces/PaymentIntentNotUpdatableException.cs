namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// Stripe refused to change a payment intent's amount because of its state — it is processing, has succeeded or was
/// cancelled — so the customer is being, or has been, charged the old amount. See
/// <see cref="IStripePaymentService.UpdatePaymentIntentAmountAsync"/>.
/// </summary>
public sealed class PaymentIntentNotUpdatableException : Exception
{
    public PaymentIntentNotUpdatableException(string paymentIntentId, string stripeMessage, Exception? innerException = null)
        : base($"Payment intent {paymentIntentId} cannot be updated: {stripeMessage}", innerException)
    {
        PaymentIntentId = paymentIntentId;
        StripeMessage = stripeMessage;
    }

    public string PaymentIntentId { get; }

    /// <summary>Stripe's own explanation, e.g. "This PaymentIntent's amount could not be updated because it has a status of succeeded".</summary>
    public string StripeMessage { get; }
}
