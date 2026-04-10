using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Application.Payments.Commands.RefundPayment;

public sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;

    public RefundPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentProcessor paymentProcessor,
        IStripePaymentService stripePaymentService,
        IIntegrationEventOutbox integrationEventOutbox,
        IUnitOfWork unitOfWork,
        ILogger<RefundPaymentCommandHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _paymentProcessor = paymentProcessor;
        _stripePaymentService = stripePaymentService;
        _integrationEventOutbox = integrationEventOutbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
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

        var refundResult = string.Equals(payment.PaymentMethod, "Stripe", StringComparison.OrdinalIgnoreCase)
            ? await RefundStripePaymentAsync(payment, refundAmount, cancellationToken)
            : await _paymentProcessor.RefundPaymentAsync(payment.PaymentIntentId, refundAmount, cancellationToken);

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
        _integrationEventOutbox.Enqueue(new PaymentRefundedEvent
        {
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            PaymentIntentId = payment.PaymentIntentId,
            Amount = refundAmount,
            RefundedAt = payment.ProcessedAt ?? DateTime.UtcNow
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PaymentDto>.Success(payment.ToDto());
    }

    private async Task<PaymentResult> RefundStripePaymentAsync(
        PaymentTransaction payment,
        decimal refundAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            var stripeRefund = await _stripePaymentService.CreateRefundAsync(
                payment.PaymentIntentId,
                refundAmount,
                payment.Currency,
                cancellationToken);

            if (string.Equals(stripeRefund.Status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stripeRefund.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentResult.Failed($"Stripe refund failed with status '{stripeRefund.Status}'.");
            }

            return PaymentResult.Successful(payment.PaymentIntentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe refund failed for payment {PaymentId} (intent {PaymentIntentId})", payment.Id, payment.PaymentIntentId);
            return PaymentResult.Failed("An error occurred while processing the refund. Please try again later.");
        }
    }
}
