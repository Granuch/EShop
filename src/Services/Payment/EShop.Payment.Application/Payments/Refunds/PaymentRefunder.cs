using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Application.Payments.Refunds;

public sealed class PaymentRefunder : IPaymentRefunder
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly ILogger<PaymentRefunder> _logger;

    public PaymentRefunder(
        IPaymentRepository paymentRepository,
        IPaymentProcessor paymentProcessor,
        IStripePaymentService stripePaymentService,
        IIntegrationEventOutbox integrationEventOutbox,
        ILogger<PaymentRefunder> logger)
    {
        _paymentRepository = paymentRepository;
        _paymentProcessor = paymentProcessor;
        _stripePaymentService = stripePaymentService;
        _integrationEventOutbox = integrationEventOutbox;
        _logger = logger;
    }

    public async Task RefundInFullAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        if (string.Equals(payment.PaymentMethod, "Stripe", StringComparison.OrdinalIgnoreCase))
        {
            var refund = await _stripePaymentService.CreateRefundAsync(
                payment.PaymentIntentId,
                payment.Amount,
                payment.Currency,
                cancellationToken);

            if (string.Equals(refund.Status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(refund.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentRefundRefusedException(payment.Id, $"Stripe refund failed with status '{refund.Status}'.");
            }

            if (refund.AlreadyRefunded)
            {
                _logger.LogInformation(
                    "Stripe reports intent {PaymentIntentId} of payment {PaymentId} as already refunded; recording the refund.",
                    payment.PaymentIntentId,
                    payment.Id);
            }
        }
        else
        {
            var result = await _paymentProcessor.RefundPaymentAsync(payment.PaymentIntentId, payment.Amount, cancellationToken);
            if (!result.Success)
            {
                throw new PaymentRefundRefusedException(payment.Id, result.ErrorMessage ?? "Refund failed.");
            }
        }

        var now = DateTime.UtcNow;
        payment.Status = PaymentStatus.Refunded;
        payment.UpdatedAt = now;
        payment.ProcessedAt = now;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _integrationEventOutbox.Enqueue(new PaymentRefundedEvent
        {
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            PaymentIntentId = payment.PaymentIntentId,
            Amount = payment.Amount,
            RefundedAt = now
        });
    }
}
