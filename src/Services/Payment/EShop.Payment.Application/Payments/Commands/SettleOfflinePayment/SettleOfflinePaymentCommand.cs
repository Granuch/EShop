using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.SettleOfflinePayment;

/// <summary>
/// An operator records that an order was paid outside the system — bank transfer, cash at the counter (Admin panel
/// S10, endpoint #58, decision Q6a).
///
/// <para>
/// <b>Why this lives in Payment and not in Ordering.</b> Q6a rejected an <c>Order.MarkAsPaidManually</c>, so
/// <c>Order.MarkAsPaid</c> keeps its single caller: this command settles Payment's own record of the order and the
/// existing <c>PaymentSuccessEvent</c> carries the fact across. The order therefore turns Paid <i>asynchronously</i>,
/// one outbox poll later, exactly as a Stripe payment does.
/// </para>
///
/// <para>
/// <b>There is no amount on the request, deliberately.</b> The payment is settled for the amount Payment recorded from
/// <c>OrderCreatedEvent</c>, like <c>POST /api/v1/payments</c> and <c>/create-intent</c> before it. An
/// operator-supplied amount would reach <c>Order.MarkAsPaid</c>, which throws on any mismatch — so a typo would not be
/// refused at the API, it would dead-letter the message in Ordering long after this request answered 200.
/// </para>
///
/// <para>
/// <b>And no free-text note.</b> The only column one could go in is <c>ErrorMessage</c>, and a Success row carrying an
/// "error message" misleads every reader and every dashboard. An operator note belongs on the payment event timeline
/// S11 adds.
/// </para>
/// </summary>
/// <param name="Reference">
/// The operator's own evidence: a bank transfer reference, a receipt number. Required — an offline payment with
/// nothing to reconcile against is unauditable — and unique, because it is stored in <c>PaymentIntentId</c> under the
/// <c>offline:</c> prefix, where the filtered unique index already guarantees one intent per payment.
/// </param>
public sealed record SettleOfflinePaymentCommand(Guid OrderId, string Reference)
    : IRequest<Result<PaymentDto>>, ITransactionalCommand
{
    /// <summary>Bounded well under the 200-character <c>PaymentIntentId</c> column, which also has to hold the
    /// <c>offline:</c> prefix and be storable on the order that receives it.</summary>
    public const int MaxReferenceLength = 100;
}
