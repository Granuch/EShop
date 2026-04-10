using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePayment;

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentProcessor paymentProcessor,
        IIntegrationEventOutbox integrationEventOutbox,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _paymentProcessor = paymentProcessor;
        _integrationEventOutbox = integrationEventOutbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentDto>> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var existing = await _paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (existing is not null)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_ALREADY_EXISTS",
                "Payment already exists for this order."));
        }

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            UserId = request.UserId,
            Amount = request.Amount,
            Currency = (request.Currency ?? "USD").ToUpperInvariant(),
            PaymentMethod = request.PaymentMethod ?? "Mock",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _paymentRepository.AddAsync(payment, cancellationToken);
        _integrationEventOutbox.Enqueue(new PaymentCreatedEvent
        {
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString().ToUpperInvariant(),
            CreatedAt = payment.CreatedAt
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var result = await _paymentProcessor.ProcessPaymentAsync(
            payment.OrderId,
            payment.Amount,
            cancellationToken);

        if (result.Success)
        {
            payment.Status = PaymentStatus.Success;
            payment.PaymentIntentId = result.PaymentIntentId ?? string.Empty;
            payment.ErrorMessage = null;
            payment.ProcessedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _integrationEventOutbox.Enqueue(new PaymentSuccessEvent
            {
                OrderId = payment.OrderId,
                PaymentIntentId = payment.PaymentIntentId,
                Amount = payment.Amount,
                ProcessedAt = payment.ProcessedAt ?? DateTime.UtcNow
            });

            _integrationEventOutbox.Enqueue(new PaymentCompletedEvent
            {
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentIntentId = payment.PaymentIntentId,
                CompletedAt = payment.ProcessedAt ?? DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PaymentDto>.Success(payment.ToDto());
        }

        payment.Status = PaymentStatus.Failed;
        payment.ErrorMessage = result.ErrorMessage ?? "Unknown payment processing error";
        payment.ProcessedAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _integrationEventOutbox.Enqueue(new PaymentFailedEvent
        {
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Reason = payment.ErrorMessage,
            FailedAt = payment.ProcessedAt ?? DateTime.UtcNow
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PaymentDto>.Success(payment.ToDto());
    }
}
