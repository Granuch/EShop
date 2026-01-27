namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// Interface for payment processing
/// </summary>
public interface IPaymentProcessor
{
    // TODO: Implement payment processing (mock or real integration)
    Task<PaymentResult> ProcessPaymentAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default);

    // TODO: Implement refund
    Task<PaymentResult> RefundPaymentAsync(string paymentIntentId, decimal amount, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of payment operation
/// </summary>
public record PaymentResult
{
    public bool Success { get; init; }
    public string? PaymentIntentId { get; init; }
    public string? ErrorMessage { get; init; }

    public static PaymentResult Successful(string paymentIntentId) =>
        new() { Success = true, PaymentIntentId = paymentIntentId };

    public static PaymentResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
}
