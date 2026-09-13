namespace EShop.Payment.Application.Payments.Abstractions;

/// <summary>
/// Stripe could not be reached, or answered with an error that retrying can fix (a timeout, 429, 5xx, or an
/// idempotent request still in progress). Payment audit Stage 6 (H2): nothing about the payment is decided by it.
/// The request's transaction rolls back and the caller retries. <c>/create-intent</c> answers it with 503.
/// </summary>
public sealed class PaymentProviderUnavailableException : Exception
{
    public PaymentProviderUnavailableException(string operation, Exception innerException)
        : base($"The payment provider is unavailable ({operation}).", innerException)
    {
        Operation = operation;
    }

    public string Operation { get; }
}
