namespace EShop.Payment.Infrastructure.Configuration;

/// <summary>
/// Ordering audit Stage 19 (decision D16). What <c>OrderCancelledConsumer</c> does when a cancelled
/// order's money has already been taken — the payment was captured, or Stripe refused to cancel an
/// intent because it had just succeeded.
///
/// <para>Off (the default): the message is thrown into <c>payment_order_cancelled_error</c> for a person
/// to refund by hand (the local runbook). On: the payment is refunded in full there and then, through the
/// same code as the admin refund endpoint; only a refund that fails still reaches the error queue.</para>
///
/// <para>Configured as <c>CancelledOrders:AutoRefund</c> (<c>CancelledOrders__AutoRefund</c> in the
/// environment). Refunds are full-only (D11), so there is nothing else to configure.</para>
/// </summary>
public sealed class CancelledOrderRefundSettings
{
    public const string SectionName = "CancelledOrders";

    public bool AutoRefund { get; init; }
}
