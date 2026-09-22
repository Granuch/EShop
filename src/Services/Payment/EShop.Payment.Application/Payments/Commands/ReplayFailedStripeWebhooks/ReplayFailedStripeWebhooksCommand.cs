using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.ReplayFailedStripeWebhooks;

/// <summary>
/// Re-applies Stripe webhook deliveries this service accepted and then failed to apply
/// (<c>POST /api/v1/payments/webhooks/failed/replay</c>, Admin panel S11, endpoint #67).
///
/// <para>
/// <b>Deliberately NOT an <c>ITransactionalCommand</c>.</b> Every other write in this service is one, so the omission
/// reads as a slip and is the opposite. A batch wraps many independent units of work: inside one transaction, the
/// first row that fails leaves Postgres refusing every later statement (<c>25P02</c>), so rows two to ten would be
/// reported as failures of a transaction rather than of themselves — and a per-row report that lies is worse than no
/// report. <see cref="IFailedStripeWebhookReplayer"/> gives each row its own scope instead.
/// </para>
///
/// <para>
/// <b>Idempotence is inherited, not invented.</b> Each replay goes through the same <c>IStripeWebhookProcessor</c> the
/// live endpoint uses, so <c>ProcessedStripeWebhookEvents</c> refuses an event that has already been applied —
/// however many times an operator presses the button, and whether or not Stripe's own redelivery got there first.
/// </para>
/// </summary>
/// <param name="Ids">
/// The captures to replay, or omitted/empty for "every outstanding one, oldest first". Follows the BUG-09 three-case
/// rule the wrong way round on purpose: there is no way to say "replay nothing", because that is what not calling the
/// endpoint means.
/// </param>
public sealed record ReplayFailedStripeWebhooksCommand(IReadOnlyCollection<Guid>? Ids)
    : IRequest<Result<FailedStripeWebhookReplayDto>>, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "StripeWebhook";

    string? IAuditedCommand.AuditEntityId => null;

    /// <summary>
    /// The hard cap on one request (risk A8).
    /// <para>Lower than Basket's <c>MaxReplayPerRequest = 1000</c> because the unit of work is far heavier: each row
    /// re-runs a whole webhook, which reads and writes a payment, inserts a processed-event row and may enqueue two
    /// integration events. A hundred of those is already a long synchronous request; a thousand is a request that
    /// times out halfway and leaves an operator guessing which half ran.</para>
    /// </summary>
    public const int MaxReplayPerRequest = 100;

    public IReadOnlyCollection<Guid> EffectiveIds => Ids ?? [];
}
