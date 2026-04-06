using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MassTransit;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.RefundPayment;

public sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;

    public RefundPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentProcessor paymentProcessor,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _paymentProcessor = paymentProcessor;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentDto>> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_NOT_FOUND",
                "Payment not found."));
        }

        if (payment.Status != PaymentStatus.Success)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_ALREADY_PROCESSED",
                "Only successful payments can be refunded."));
        }

        var refundAmount = request.Amount ?? payment.Amount;
        if (refundAmount <= 0 || refundAmount > payment.Amount)
        {
            return Result<PaymentDto>.Failure(new Error(
                "INVALID_REFUND_AMOUNT",
                "Refund amount must be greater than 0 and less than or equal to original amount."));
        }

        var refundResult = await _paymentProcessor.RefundPaymentAsync(
            payment.PaymentIntentId,
            refundAmount,
            cancellationToken);

        if (!refundResult.Success)
        {
            return Result<PaymentDto>.Failure(new Error(
                "REFUND_FAILED",
                refundResult.ErrorMessage ?? "Refund failed."));
        }

        payment.Status = PaymentStatus.Refunded;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.ProcessedAt = DateTime.UtcNow;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(new PaymentRefundedEvent
        {
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            PaymentIntentId = payment.PaymentIntentId,
            Amount = refundAmount,
            RefundedAt = payment.ProcessedAt ?? DateTime.UtcNow
        }, cancellationToken);

        return Result<PaymentDto>.Success(payment.ToDto());
    }
}
