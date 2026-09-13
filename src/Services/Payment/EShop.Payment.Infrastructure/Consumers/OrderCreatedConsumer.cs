using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Payment.Infrastructure.Consumers;

/// <summary>
/// Starts the payment of a new order. Payment audit Stage 1 (D1): <c>Stripe:Enabled</c> picks the path.
/// <list type="bullet">
///   <item><b>Stripe on</b> — a Pending Stripe payment is recorded for the order total and nothing is charged:
///   the customer pays through <c>/create-intent</c>.</item>
///   <item><b>Stripe off</b> — the order is settled through the simulator (<see cref="IPaymentProcessor"/>).</item>
/// </list>
/// A payment still in flight that this consumer did not start is left to the flow that owns it.
/// </summary>
public class OrderCreatedConsumer : IdempotentConsumer<OrderCreatedEvent, PaymentDbContext>
{
    private static readonly HashSet<PaymentStatus> TerminalStatuses =
    [
        PaymentStatus.Success,
        PaymentStatus.Failed,
        PaymentStatus.Refunded,
        // OrderCancelledConsumer leaves this when a cancellation overtakes OrderCreatedEvent; without
        // it here, the late OrderCreatedEvent would charge an order that no longer exists.
        PaymentStatus.Cancelled
    ];

    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly bool _stripeEnabled;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        PaymentDbContext dbContext,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        IPaymentProcessor paymentProcessor,
        IOptions<StripeSettings> stripeOptions,
        IIntegrationEventOutbox integrationEventOutbox,
        ILogger<OrderCreatedConsumer> logger)
        : base(dbContext, logger)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _paymentProcessor = paymentProcessor;
        _stripeEnabled = stripeOptions.Value.Enabled;
        _integrationEventOutbox = integrationEventOutbox;
        _logger = logger;
    }

    protected override async Task HandleAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        var payment = await _paymentRepository.GetByOrderIdAsync(message.OrderId, cancellationToken);
        if (payment is not null && TerminalStatuses.Contains(payment.Status))
        {
            _logger.LogInformation(
                "Payment already finalized for OrderId={OrderId}. Status={Status}",
                message.OrderId,
                payment.Status);
            return;
        }

        // Payment audit Stage 1 (D1). A payment still in flight belongs to the flow that created it: a Stripe
        // intent from /create-intent, or the Pending record written below when Stripe is on. Re-processing it
        // through the simulator overwrote the real intent id with a fake one and marked the order Paid with
        // nothing charged. The only payment this consumer resumes is one it started through the simulator
        // itself, with Stripe off.
        if (payment is not null
            && (_stripeEnabled || payment.PaymentMethod != PaymentMethodType.Mock))
        {
            _logger.LogInformation(
                "Payment for OrderId={OrderId} is already {Status} ({PaymentMethod}); left to the flow that owns it.",
                message.OrderId,
                payment.Status,
                payment.PaymentMethod);
            return;
        }

        if (payment is null && _stripeEnabled)
        {
            // D1: with Stripe on, the customer pays through /create-intent, which charges this record's amount
            // (D4). Nothing is charged or announced here.
            await _paymentRepository.AddAsync(
                PaymentTransaction.RecordForOrder(
                    message.OrderId, message.UserId, message.TotalAmount, PaymentMethodType.Stripe, DateTime.UtcNow),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recorded a pending Stripe payment of {Amount} USD for OrderId={OrderId}; the customer pays through create-intent.",
                message.TotalAmount,
                message.OrderId);
            return;
        }

        if (payment is null)
        {
            payment = PaymentTransaction.RecordForOrder(
                message.OrderId, message.UserId, message.TotalAmount, PaymentMethodType.Mock, DateTime.UtcNow);

            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        payment.StartSimulated(DateTime.UtcNow);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _integrationEventOutbox.Enqueue(new PaymentCreatedEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString().ToUpperInvariant(),
            CreatedAt = payment.CreatedAt
        }, message.CorrelationId);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Processing payment for OrderId={OrderId}, UserId={UserId}, Amount={Amount}",
            message.OrderId,
            message.UserId,
            message.TotalAmount);

        var result = await _paymentProcessor.ProcessPaymentAsync(
            message.OrderId,
            message.TotalAmount,
            cancellationToken);

        if (result.Success)
        {
            payment.RecordSimulatedSuccess(result.PaymentIntentId ?? string.Empty, DateTime.UtcNow);

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _integrationEventOutbox.Enqueue(new PaymentSuccessEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = message.OrderId,
                PaymentIntentId = result.PaymentIntentId ?? string.Empty,
                Amount = message.TotalAmount,
                ProcessedAt = DateTime.UtcNow
            }, message.CorrelationId);

            _integrationEventOutbox.Enqueue(new PaymentCompletedEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentIntentId = payment.PaymentIntentId,
                CompletedAt = payment.ProcessedAt ?? DateTime.UtcNow
            }, message.CorrelationId);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Payment successful for OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
                message.OrderId,
                result.PaymentIntentId);

            return;
        }

        payment.RecordSimulatedFailure(result.ErrorMessage ?? "Unknown payment processing error", DateTime.UtcNow);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _integrationEventOutbox.Enqueue(new PaymentFailedEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = message.OrderId,
            UserId = message.UserId,
            Reason = payment.ErrorMessage,
            FailedAt = DateTime.UtcNow
        }, message.CorrelationId);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Payment failed for OrderId={OrderId}. Reason={Reason}",
            message.OrderId,
            result.ErrorMessage);
    }
}
