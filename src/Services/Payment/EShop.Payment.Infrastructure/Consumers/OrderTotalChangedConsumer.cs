using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Infrastructure.Consumers;

/// <summary>
/// frontend-contracts F-47. <b>A payment charges the order's current total, not the one it was created with.</b>
/// <c>OrderCreatedConsumer</c> records the amount once. Before this consumer, an item added, removed or re-quantified
/// on a Pending order never reached Payment: the customer was charged the old total, and <c>Order.MarkAsPaid</c>
/// refused the success because the amounts differed, leaving a paid order Pending for good.
///
/// <para>The rule, by the payment's state when the change arrives (mirroring <see cref="OrderCancelledConsumer"/>):</para>
/// <list type="bullet">
///   <item><b>No payment yet</b> — the change overtook <c>OrderCreatedEvent</c> (separate queues).
///   <see cref="PaymentNotRecordedYetException"/> is thrown so the message is retried once the payment exists.</item>
///   <item><b>Already reflects a newer total</b> (<see cref="PaymentTransaction.AmountAsOf"/>) — an older change
///   delivered late. Ignored, so it cannot put an old total back.</item>
///   <item><b>Pending</b> — the amount is revised. Nothing has been attempted at any provider, so the next
///   <c>/create-intent</c>, or an operator's settlement, charges the new total.</item>
///   <item><b>Processing, with a Stripe intent</b> — the intent's amount is changed at Stripe first, then the record.
///   If Stripe refuses (the intent is processing, succeeded or cancelled), the old amount stands and
///   <see cref="PaymentAmountRevisionFailedException"/> sends the message to the error queue for a person.</item>
///   <item><b>Success</b>, or a <b>simulated payment in flight</b> — the old amount has been, or is being, charged.
///   The same exception, for the same reason.</item>
///   <item><b>Failed, Cancelled, Refunded</b> — the order is, or is about to be, final; nothing will be charged.
///   Nothing to do.</item>
/// </list>
/// <para>Any other failure (a Stripe outage, the database) propagates unchanged and is retried.</para>
/// </summary>
public class OrderTotalChangedConsumer : IdempotentConsumer<OrderTotalChangedEvent, PaymentDbContext>
{
    private static readonly HashSet<PaymentStatus> Closed =
    [
        PaymentStatus.Failed,
        PaymentStatus.Refunded,
        PaymentStatus.Cancelled
    ];

    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStripePaymentService _stripePaymentService;

    public OrderTotalChangedConsumer(
        PaymentDbContext dbContext,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        ILogger<OrderTotalChangedConsumer> logger)
        : base(dbContext, logger)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _stripePaymentService = stripePaymentService;
    }

    protected override async Task HandleAsync(ConsumeContext<OrderTotalChangedEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;
        var now = DateTime.UtcNow;

        var payment = await _paymentRepository.GetByOrderIdAsync(message.OrderId, cancellationToken)
            ?? throw new PaymentNotRecordedYetException(message.OrderId);

        if (payment.AlreadyReflectsTotalAsOf(message.TotalAsOf))
        {
            Logger.LogInformation(
                "Ignoring the total {NewTotal} for OrderId={OrderId} as of {TotalAsOf:O}: payment {PaymentId} already reflects the total as of {AmountAsOf:O}.",
                message.NewTotal,
                message.OrderId,
                message.TotalAsOf,
                payment.Id,
                payment.AmountAsOf);
            return;
        }

        if (Closed.Contains(payment.Status))
        {
            Logger.LogInformation(
                "Payment {PaymentId} for OrderId={OrderId} is already {Status}; its amount is not revised to {NewTotal}.",
                payment.Id,
                message.OrderId,
                payment.Status,
                message.NewTotal);
            return;
        }

        if (!string.Equals(message.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentAmountRevisionFailedException(
                message.OrderId,
                $"the new total is in {message.Currency}, and payment {payment.Id} is in {payment.Currency}.");
        }

        var stripeInFlight = payment.Status == PaymentStatus.Processing
            && payment.PaymentMethod == PaymentMethodType.Stripe
            && !string.IsNullOrEmpty(payment.PaymentIntentId);

        if (payment.Status != PaymentStatus.Pending && !stripeInFlight)
        {
            throw new PaymentAmountRevisionFailedException(
                message.OrderId,
                $"payment {payment.Id} is {payment.Status} ({payment.PaymentMethod}, intent '{payment.PaymentIntentId}') "
                + $"for {payment.Amount} {payment.Currency}; the order now totals {message.NewTotal}.");
        }

        // Stripe BEFORE the record, as OrderCancelledConsumer does: if Stripe refuses, the record must still say what
        // the customer is actually being asked to pay, and the exception rolls back the consumer's transaction.
        if (stripeInFlight && payment.Amount != message.NewTotal)
        {
            try
            {
                var stripeStatus = await _stripePaymentService.UpdatePaymentIntentAmountAsync(
                    payment.PaymentIntentId, message.NewTotal, payment.Currency, cancellationToken);
                payment.ObserveStripeStatus(stripeStatus, now);
            }
            catch (PaymentIntentNotUpdatableException ex)
            {
                throw new PaymentAmountRevisionFailedException(
                    message.OrderId,
                    $"Stripe refused to change intent '{payment.PaymentIntentId}' from {payment.Amount} to {message.NewTotal}: {ex.StripeMessage}",
                    ex);
            }
        }

        var previousAmount = payment.Amount;
        payment.ReviseAmount(message.NewTotal, message.TotalAsOf, now);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation(
            "Payment {PaymentId} for OrderId={OrderId} revised from {PreviousAmount} to {NewTotal} {Currency} (intent '{PaymentIntentId}').",
            payment.Id,
            message.OrderId,
            previousAmount,
            message.NewTotal,
            payment.Currency,
            payment.PaymentIntentId);
    }
}
