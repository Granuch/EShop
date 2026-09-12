using System.Globalization;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Application.Payments.Refunds;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Application.Payments.Commands.RefundPayment;

public sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentRefunder _paymentRefunder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;

    public RefundPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentRefunder paymentRefunder,
        IUnitOfWork unitOfWork,
        ILogger<RefundPaymentCommandHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _paymentRefunder = paymentRefunder;
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

        // Ordering audit Stage 11. Full refunds only. A partial refund used to mark the whole payment
        // Refunded, which blocked any further refund and told Ordering all the money had been returned.
        if (request.Amount is { } requested && requested != payment.Amount)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PARTIAL_REFUND_NOT_SUPPORTED",
                $"Only a full refund of {payment.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency} is supported."));
        }

        // Ordering audit Stage 19: the refund itself is shared with OrderCancelledConsumer's automatic
        // refund. The endpoint reports every failure as REFUND_FAILED; the provider's own message for an
        // unexpected exception stays in the log.
        try
        {
            await _paymentRefunder.RefundInFullAsync(payment, cancellationToken);
        }
        catch (PaymentRefundRefusedException ex)
        {
            return Result<PaymentDto>.Failure(new Error("REFUND_FAILED", ex.Reason));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Refund failed for payment {PaymentId} (intent {PaymentIntentId})", payment.Id, payment.PaymentIntentId);
            return Result<PaymentDto>.Failure(new Error(
                "REFUND_FAILED",
                "An error occurred while processing the refund. Please try again later."));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PaymentDto>.Success(payment.ToDto());
    }
}
