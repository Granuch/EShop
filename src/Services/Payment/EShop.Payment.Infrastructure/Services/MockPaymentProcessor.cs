using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Payment.Infrastructure.Services;

/// <summary>
/// Mock payment processor (80% success rate)
/// </summary>
public class MockPaymentProcessor : IPaymentProcessor
{
    private readonly Random _random;
    private readonly ILogger<MockPaymentProcessor> _logger;
    private readonly PaymentSimulationSettings _settings;

    public MockPaymentProcessor(
        IOptions<PaymentSimulationSettings> settings,
        ILogger<MockPaymentProcessor> logger)
    {
        _logger = logger;
        _settings = settings.Value;
        _random = _settings.RandomSeed.HasValue
            ? new Random(_settings.RandomSeed.Value)
            : new Random();
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid orderId, 
        decimal amount, 
        CancellationToken cancellationToken = default)
    {
        var minDelay = Math.Max(0, _settings.ProcessingDelayMinSeconds);
        var maxDelay = Math.Max(minDelay + 1, _settings.ProcessingDelayMaxSeconds + 1);
        var delaySeconds = _random.Next(minDelay, maxDelay);

        _logger.LogInformation(
            "Starting mock payment processing for OrderId={OrderId}, Amount={Amount}, DelaySeconds={DelaySeconds}",
            orderId,
            amount,
            delaySeconds);

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

        var successRate = Math.Clamp(_settings.SuccessRatePercent, 0, 100);
        var isSuccess = _settings.Mode switch
        {
            PaymentSimulationMode.AlwaysSuccess => true,
            PaymentSimulationMode.AlwaysFailure => false,
            _ => _random.Next(1, 101) <= successRate
        };

        if (isSuccess)
        {
            var paymentIntentId = $"pi_{Guid.NewGuid():N}";
            _logger.LogInformation(
                "Mock payment successful for OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
                orderId,
                paymentIntentId);

            return PaymentResult.Successful(paymentIntentId);
        }

        var errors = new[]
        {
            "Insufficient funds",
            "Card declined",
            "Invalid card number",
            "Card expired"
        };
        var error = _settings.Mode == PaymentSimulationMode.AlwaysFailure
            ? _settings.ForcedFailureReason
            : errors[_random.Next(errors.Length)];

        _logger.LogWarning(
            "Mock payment failed for OrderId={OrderId}. Reason={Reason}",
            orderId,
            error);

        return PaymentResult.Failed(error);
    }

    public async Task<PaymentResult> RefundPaymentAsync(
        string paymentIntentId, 
        decimal amount, 
        CancellationToken cancellationToken = default)
    {
        var refundDelay = Math.Max(0, _settings.RefundDelaySeconds);

        _logger.LogInformation(
            "Starting mock refund for PaymentIntentId={PaymentIntentId}, Amount={Amount}, DelaySeconds={DelaySeconds}",
            paymentIntentId,
            amount,
            refundDelay);

        await Task.Delay(TimeSpan.FromSeconds(refundDelay), cancellationToken);

        _logger.LogInformation(
            "Mock refund successful for PaymentIntentId={PaymentIntentId}",
            paymentIntentId);

        return PaymentResult.Successful(paymentIntentId);
    }
}
