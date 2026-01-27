using EShop.Payment.Domain.Interfaces;

namespace EShop.Payment.Infrastructure.Services;

/// <summary>
/// Mock payment processor (80% success rate)
/// </summary>
public class MockPaymentProcessor : IPaymentProcessor
{
    private readonly Random _random = new();
    // TODO: Inject ILogger
    // private readonly ILogger<MockPaymentProcessor> _logger;

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid orderId, 
        decimal amount, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Simulate payment processing delay (1-3 seconds)
        await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 4)), cancellationToken);

        // TODO: 80% success rate
        var isSuccess = _random.Next(1, 101) <= 80;

        if (isSuccess)
        {
            // TODO: Generate mock payment intent ID
            var paymentIntentId = $"pi_{Guid.NewGuid():N}";
            return PaymentResult.Successful(paymentIntentId);
        }
        else
        {
            // TODO: Return random failure reason
            var errors = new[]
            {
                "Insufficient funds",
                "Card declined",
                "Invalid card number",
                "Card expired"
            };
            return PaymentResult.Failed(errors[_random.Next(errors.Length)]);
        }
    }

    public async Task<PaymentResult> RefundPaymentAsync(
        string paymentIntentId, 
        decimal amount, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement mock refund logic
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return PaymentResult.Successful(paymentIntentId);
    }
}
