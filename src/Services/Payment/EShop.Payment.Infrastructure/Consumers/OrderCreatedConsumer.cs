using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Infrastructure.Consumers;

public class OrderCreatedConsumer : IdempotentConsumer<OrderCreatedEvent, PaymentDbContext>
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
        PaymentDbContext dbContext,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        IPaymentProcessor paymentProcessor,
        IPublishEndpoint publishEndpoint,
        ILogger<OrderCreatedConsumer> logger)
        : base(dbContext, logger)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _paymentProcessor = paymentProcessor;
        _publishEndpoint = publishEndpoint;
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

            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        payment.Status = PaymentStatus.Processing;
        payment.UpdatedAt = DateTime.UtcNow;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(new PaymentCreatedEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString().ToUpperInvariant(),
            CreatedAt = payment.CreatedAt
        }, cancellationToken);

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
            payment.Status = PaymentStatus.Success;
            payment.PaymentIntentId = result.PaymentIntentId ?? string.Empty;
            payment.ErrorMessage = null;
            payment.ProcessedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _publishEndpoint.Publish(new PaymentSuccessEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = message.OrderId,
                PaymentIntentId = result.PaymentIntentId ?? string.Empty,
                Amount = message.TotalAmount,
                ProcessedAt = DateTime.UtcNow
            }, cancellationToken);

            await _publishEndpoint.Publish(new PaymentCompletedEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentIntentId = payment.PaymentIntentId,
                CompletedAt = payment.ProcessedAt ?? DateTime.UtcNow
            }, cancellationToken);

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
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(new PaymentFailedEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = message.OrderId,
            UserId = message.UserId,
            Reason = payment.ErrorMessage,
            FailedAt = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogWarning(
            "Payment failed for OrderId={OrderId}. Reason={Reason}",
            message.OrderId,
            result.ErrorMessage);
    }
}
