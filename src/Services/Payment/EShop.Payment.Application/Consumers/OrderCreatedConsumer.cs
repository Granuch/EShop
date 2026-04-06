using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.BuildingBlocks.Domain;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Application.Consumers;

/// <summary>
/// Consumer for OrderCreatedEvent - processes payment
/// </summary>
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private static readonly HashSet<PaymentStatus> TerminalStatuses =
    [
        PaymentStatus.Success,
        PaymentStatus.Failed,
        PaymentStatus.Refunded
    ];
    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        IPaymentProcessor paymentProcessor,
        IPublishEndpoint publishEndpoint,
        ILogger<OrderCreatedConsumer> logger)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _paymentProcessor = paymentProcessor;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;

        var payment = await _paymentRepository.GetByOrderIdAsync(message.OrderId, context.CancellationToken);
        if (payment is not null && TerminalStatuses.Contains(payment.Status))
        {
            _logger.LogInformation(
                "Payment already finalized for OrderId={OrderId}. Status={Status}",
                message.OrderId,
                payment.Status);
            return;
        }

        if (payment is null)
        {
            payment = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = message.OrderId,
                UserId = message.UserId,
                Amount = message.TotalAmount,
                Currency = "USD",
                PaymentMethod = "Mock",
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _paymentRepository.AddAsync(payment, context.CancellationToken);
        }

        payment.Status = PaymentStatus.Processing;
        payment.UpdatedAt = DateTime.UtcNow;
        await _paymentRepository.UpdateAsync(payment, context.CancellationToken);
        await _unitOfWork.SaveChangesAsync(context.CancellationToken);

        await _publishEndpoint.Publish(new PaymentCreatedEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString().ToUpperInvariant(),
            CreatedAt = payment.CreatedAt
        }, context.CancellationToken);

        _logger.LogInformation(
            "Processing payment for OrderId={OrderId}, UserId={UserId}, Amount={Amount}",
            message.OrderId,
            message.UserId,
            message.TotalAmount);

        var result = await _paymentProcessor.ProcessPaymentAsync(
            message.OrderId,
            message.TotalAmount,
            context.CancellationToken);

        if (result.Success)
        {
            payment.Status = PaymentStatus.Success;
            payment.PaymentIntentId = result.PaymentIntentId ?? string.Empty;
            payment.ErrorMessage = null;
            payment.ProcessedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, context.CancellationToken);
            await _unitOfWork.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PaymentSuccessEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = message.OrderId,
                PaymentIntentId = result.PaymentIntentId ?? string.Empty,
                Amount = message.TotalAmount,
                ProcessedAt = DateTime.UtcNow
            }, context.CancellationToken);

            await _publishEndpoint.Publish(new PaymentCompletedEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentIntentId = payment.PaymentIntentId,
                CompletedAt = payment.ProcessedAt ?? DateTime.UtcNow
            }, context.CancellationToken);

            _logger.LogInformation(
                "Payment successful for OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
                message.OrderId,
                result.PaymentIntentId);

            return;
        }

        payment.Status = PaymentStatus.Failed;
        payment.ErrorMessage = result.ErrorMessage ?? "Unknown payment processing error";
        payment.ProcessedAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;
        await _paymentRepository.UpdateAsync(payment, context.CancellationToken);
        await _unitOfWork.SaveChangesAsync(context.CancellationToken);

        await _publishEndpoint.Publish(new PaymentFailedEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = message.OrderId,
            UserId = message.UserId,
            Reason = payment.ErrorMessage,
            FailedAt = DateTime.UtcNow
        }, context.CancellationToken);

        _logger.LogWarning(
            "Payment failed for OrderId={OrderId}. Reason={Reason}",
            message.OrderId,
            result.ErrorMessage);
    }
}
