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
        // Payment audit Stage 3 (H4, D2). Every order's payment is recorded from OrderCreatedEvent (D1), so this
        // admin tool settles that record, for its amount. It used to create a payment from the request, which let a
        // customer settle any order through the simulator for any amount.
        var payment = await _paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_NOT_FOUND",
                "No payment has been recorded for this order."));
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_NOT_PENDING",
                "Only a pending payment can be settled."));
        }

        payment.StartSimulated(DateTime.UtcNow);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
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
            payment.RecordSimulatedSuccess(result.PaymentIntentId ?? string.Empty, DateTime.UtcNow);

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

        payment.RecordSimulatedFailure(result.ErrorMessage ?? "Unknown payment processing error", DateTime.UtcNow);

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
