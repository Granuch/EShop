# 💳 Payment Service

Mock сервіс обробки платежів з підтримкою event-driven patterns.

---

## Огляд

Payment Service відповідає за:
- ✅ Mock payment processing (Stripe/PayPal імітація)
- ✅ Consuming OrderCreatedEvent
- ✅ Publishing PaymentSuccessEvent / PaymentFailedEvent
- ✅ Webhook handling (для реальних payment providers)
- ✅ Retry logic для failed payments
- ✅ Payment history

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 9.0 | Web API |
| **Database** | PostgreSQL (optional) | Payment history |
| **Message Bus** | RabbitMQ + MassTransit | Event-driven |
| **Resilience** | Polly | Retry logic |

---

## Payment Processing Flow

```
OrderCreatedEvent → PaymentService → Process Payment → Publish Event
                                            ↓
                                      Success / Failed
                                            ↓
                           OrderingService updates Order status
```

---

## Event Consumers

### OrderCreatedConsumer

```csharp
// EShop.Payment.Application/Consumers/OrderCreatedConsumer.cs

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        _logger.LogInformation(
            "Processing payment for order {OrderId}", 
            context.Message.OrderId);

        try
        {
            // Mock payment processing (80% success rate)
            var result = await _paymentProcessor.ProcessPaymentAsync(
                context.Message.OrderId,
                context.Message.TotalAmount);

            if (result.Success)
            {
                await _publishEndpoint.Publish(new PaymentSuccessEvent
                {
                    OrderId = context.Message.OrderId,
                    PaymentIntentId = result.PaymentIntentId,
                    Amount = context.Message.TotalAmount,
                    ProcessedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Payment successful for order {OrderId}", 
                    context.Message.OrderId);
            }
            else
            {
                await _publishEndpoint.Publish(new PaymentFailedEvent
                {
                    OrderId = context.Message.OrderId,
                    Reason = result.ErrorMessage,
                    FailedAt = DateTime.UtcNow
                });

                _logger.LogWarning("Payment failed for order {OrderId}: {Reason}",
                    context.Message.OrderId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment for order {OrderId}", 
                context.Message.OrderId);
            throw; // MassTransit will retry
        }
    }
}
```

---

## Mock Payment Processor

```csharp
// EShop.Payment.Infrastructure/Services/MockPaymentProcessor.cs

public class MockPaymentProcessor : IPaymentProcessor
{
    private readonly Random _random = new();

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid orderId, 
        decimal amount)
    {
        // Simulate processing delay
        await Task.Delay(TimeSpan.FromSeconds(2));

        // 80% success rate
        var isSuccess = _random.Next(1, 101) <= 80;

        if (isSuccess)
        {
            return PaymentResult.Success($"pi_{Guid.NewGuid():N}");
        }
        else
        {
            var errors = new[]
            {
                "Insufficient funds",
                "Card declined",
                "Invalid card number",
                "Card expired"
            };

            return PaymentResult.Failure(errors[_random.Next(errors.Length)]);
        }
    }
}

public record PaymentResult
{
    public bool Success { get; init; }
    public string? PaymentIntentId { get; init; }
    public string? ErrorMessage { get; init; }

    public static PaymentResult Success(string paymentIntentId) =>
        new() { Success = true, PaymentIntentId = paymentIntentId };

    public static PaymentResult Failure(string error) =>
        new() { Success = false, ErrorMessage = error };
}
```

---

## Events

### PaymentSuccessEvent

```csharp
public record PaymentSuccessEvent
{
    public Guid OrderId { get; init; }
    public string PaymentIntentId { get; init; }
    public decimal Amount { get; init; }
    public DateTime ProcessedAt { get; init; }
}
```

### PaymentFailedEvent

```csharp
public record PaymentFailedEvent
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; }
    public DateTime FailedAt { get; init; }
}
```

---

## Real Payment Integration (Future)

### Stripe Integration

```csharp
public class StripePaymentProcessor : IPaymentProcessor
{
    private readonly StripeClient _stripeClient;

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid orderId, 
        decimal amount)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100), // Stripe uses cents
            Currency = "usd",
            Metadata = new Dictionary<string, string>
            {
                { "order_id", orderId.ToString() }
            }
        };

        var service = new PaymentIntentService(_stripeClient);
        var intent = await service.CreateAsync(options);

        return PaymentResult.Success(intent.Id);
    }
}
```

---

## Configuration

```json
{
  "PaymentProvider": "Mock",
  
  "Stripe": {
    "SecretKey": "sk_test_...",
    "PublicKey": "pk_test_..."
  },
  
  "RabbitMQ": {
    "Host": "localhost"
  }
}
```

---

## Наступні кроки

- ✅ [Notification Service](notification-service.md)
- ✅ [Resilience Patterns](../../05-infrastructure/resilience.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
