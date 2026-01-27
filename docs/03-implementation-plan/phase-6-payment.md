# 💳 Phase 6: Payment Service Implementation

**Duration**: 1.5 weeks  
**Team Size**: 1-2 developers  
**Prerequisites**: Phase 5 (Ordering) completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Payment processing (Stripe integration)
- ✅ Payment status tracking
- ✅ Webhook handling for payment confirmations
- ✅ Refund functionality
- ✅ Payment history
- ✅ Idempotency for payment requests

---

## Domain Model

### Entities
- **Payment**: Aggregate root
- **PaymentStatus**: Enum (Pending, Success, Failed, Refunded)

---

## Tasks Breakdown

### 6.1 Stripe Integration

**Estimated Time**: 2 days

**NuGet Packages:**

```xml
<PackageReference Include="Stripe.net" Version="43.0.0" />
```

**Payment Entity:**

```csharp
// EShop.Payment.Domain/Entities/Payment.cs

public class Payment : AggregateRoot
{
    public Guid OrderId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; }
    public string PaymentMethod { get; private set; } = string.Empty;
    public string? StripePaymentIntentId { get; private set; }
    public string? StripeChargeId { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public string? FailureReason { get; private set; }

    private Payment() { }

    public static Payment Create(Guid orderId, string userId, decimal amount, string paymentMethod)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = userId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkAsSuccess(string stripePaymentIntentId, string stripeChargeId)
    {
        Status = PaymentStatus.Success;
        StripePaymentIntentId = stripePaymentIntentId;
        StripeChargeId = stripeChargeId;
        PaidAt = DateTime.UtcNow;
        
        AddDomainEvent(new PaymentSuccessEvent(OrderId, Id, Amount));
    }

    public void MarkAsFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        
        AddDomainEvent(new PaymentFailedEvent(OrderId, Id, reason));
    }

    public void Refund()
    {
        if (Status != PaymentStatus.Success)
            throw new DomainException("Only successful payments can be refunded");

        Status = PaymentStatus.Refunded;
        AddDomainEvent(new PaymentRefundedEvent(OrderId, Id, Amount));
    }
}

public enum PaymentStatus
{
    Pending,
    Success,
    Failed,
    Refunded
}
```

---

### 6.2 Stripe Payment Service

**Estimated Time**: 2 days

```csharp
// EShop.Payment.Infrastructure/Services/StripePaymentService.cs

public class StripePaymentService : IPaymentService
{
    private readonly PaymentIntentService _paymentIntentService;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(ILogger<StripePaymentService> logger)
    {
        _paymentIntentService = new PaymentIntentService();
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        decimal amount,
        string currency,
        string paymentMethodId,
        Dictionary<string, string> metadata)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(amount * 100), // Convert to cents
                Currency = currency.ToLower(),
                PaymentMethod = paymentMethodId,
                Confirm = true,
                Metadata = metadata,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                }
            };

            var paymentIntent = await _paymentIntentService.CreateAsync(options);

            if (paymentIntent.Status == "succeeded")
            {
                return PaymentResult.Success(paymentIntent.Id, paymentIntent.Charges.Data[0].Id);
            }
            else
            {
                return PaymentResult.Failure($"Payment status: {paymentIntent.Status}");
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment failed");
            return PaymentResult.Failure(ex.StripeError.Message);
        }
    }

    public async Task<bool> RefundPaymentAsync(string chargeId, decimal amount)
    {
        try
        {
            var refundService = new RefundService();
            var refund = await refundService.CreateAsync(new RefundCreateOptions
            {
                Charge = chargeId,
                Amount = (long)(amount * 100)
            });

            return refund.Status == "succeeded";
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Refund failed for charge {ChargeId}", chargeId);
            return false;
        }
    }
}
```

---

### 6.3 Application Layer

**Estimated Time**: 2 days

**Process Payment Command:**

```csharp
// EShop.Payment.Application/Commands/ProcessPayment/ProcessPaymentCommand.cs

public record ProcessPaymentCommand : IRequest<Result<Guid>>
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodId { get; init; } = string.Empty; // Stripe payment method ID
    public string? IdempotencyKey { get; init; }
}

public class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand, Result<Guid>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentService _paymentService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IDistributedCache _cache;

    public async Task<Result<Guid>> Handle(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken)
    {
        // Idempotency check
        var idempotencyKey = request.IdempotencyKey ?? request.OrderId.ToString();
        var cachedResult = await _cache.GetAsync<Guid?>($"payment:{idempotencyKey}");
        
        if (cachedResult.HasValue)
        {
            return Result<Guid>.Success(cachedResult.Value);
        }

        // Create payment record
        var payment = Payment.Create(
            orderId: request.OrderId,
            userId: request.UserId,
            amount: request.Amount,
            paymentMethod: request.PaymentMethod);

        await _paymentRepository.AddAsync(payment, cancellationToken);

        // Process payment via Stripe
        var paymentResult = await _paymentService.ProcessPaymentAsync(
            amount: request.Amount,
            currency: "USD",
            paymentMethodId: request.PaymentMethodId,
            metadata: new Dictionary<string, string>
            {
                ["order_id"] = request.OrderId.ToString(),
                ["payment_id"] = payment.Id.ToString()
            });

        if (paymentResult.IsSuccess)
        {
            payment.MarkAsSuccess(paymentResult.PaymentIntentId!, paymentResult.ChargeId!);
            
            // Publish success event
            await _publishEndpoint.Publish(new PaymentSuccessEvent
            {
                OrderId = request.OrderId,
                PaymentId = payment.Id,
                Amount = request.Amount
            }, cancellationToken);
        }
        else
        {
            payment.MarkAsFailed(paymentResult.ErrorMessage!);
            
            // Publish failure event
            await _publishEndpoint.Publish(new PaymentFailedEvent
            {
                OrderId = request.OrderId,
                PaymentId = payment.Id,
                Reason = paymentResult.ErrorMessage!
            }, cancellationToken);
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);

        // Cache result for idempotency
        await _cache.SetAsync($"payment:{idempotencyKey}", payment.Id, TimeSpan.FromHours(24));

        return Result<Guid>.Success(payment.Id);
    }
}
```

---

### 6.4 Webhook Handler

**Estimated Time**: 1 day

```csharp
// EShop.Payment.API/Controllers/WebhooksController.cs

[ApiController]
[Route("api/v{version:apiVersion}/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IMediator _mediator;

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var stripeSignature = Request.Headers["Stripe-Signature"];

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                stripeSignature,
                _configuration["Stripe:WebhookSecret"]);

            switch (stripeEvent.Type)
            {
                case Events.PaymentIntentSucceeded:
                    var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                    await HandlePaymentIntentSucceeded(paymentIntent!);
                    break;

                case Events.PaymentIntentPaymentFailed:
                    var failedPaymentIntent = stripeEvent.Data.Object as PaymentIntent;
                    await HandlePaymentIntentFailed(failedPaymentIntent!);
                    break;

                case Events.ChargeRefunded:
                    var charge = stripeEvent.Data.Object as Charge;
                    await HandleChargeRefunded(charge!);
                    break;
            }

            return Ok();
        }
        catch (StripeException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task HandlePaymentIntentSucceeded(PaymentIntent paymentIntent)
    {
        var orderId = Guid.Parse(paymentIntent.Metadata["order_id"]);
        var paymentId = Guid.Parse(paymentIntent.Metadata["payment_id"]);

        var command = new MarkPaymentAsSuccessCommand
        {
            PaymentId = paymentId,
            StripePaymentIntentId = paymentIntent.Id,
            StripeChargeId = paymentIntent.Charges.Data[0].Id
        };

        await _mediator.Send(command);
    }
}
```

---

### 6.5 API Layer

**Estimated Time**: 1 day

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpPost]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
    {
        var userId = User.FindFirst("sub")?.Value!;

        var command = new ProcessPaymentCommand
        {
            OrderId = request.OrderId,
            UserId = userId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            PaymentMethodId = request.PaymentMethodId,
            IdempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault()
        };

        var result = await _mediator.Send(command);

        return result.IsSuccess 
            ? Ok(new { paymentId = result.Value }) 
            : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyPayments([FromQuery] GetUserPaymentsQuery query)
    {
        var userId = User.FindFirst("sub")?.Value!;
        query = query with { UserId = userId };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RefundPayment(Guid id)
    {
        var command = new RefundPaymentCommand { PaymentId = id };
        await _mediator.Send(command);
        return NoContent();
    }
}
```

---

## Configuration

```json
{
  "Stripe": {
    "SecretKey": "sk_test_...",
    "PublishableKey": "pk_test_...",
    "WebhookSecret": "whsec_..."
  }
}
```

---

## Success Criteria

- [x] Payments processed via Stripe
- [x] Webhook handling for async confirmations
- [x] Refunds supported
- [x] Idempotency implemented
- [x] All tests passing (> 75% coverage)

---

## Next Phase

→ [Phase 7: Notification Service Implementation](phase-7-notifications.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
