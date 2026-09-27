namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// Re-applies captured Stripe webhook deliveries (Admin panel S11, endpoint #67).
///
/// <para>
/// <b>Idempotence is not this service's job to invent.</b> A replay goes through the same
/// <c>IStripeWebhookProcessor</c> the live endpoint uses, and that checks <c>ProcessedStripeWebhookEvents</c> first —
/// so an event Stripe's own redelivery already applied is recognised as a duplicate and changes nothing, however many
/// times an operator presses the button. That is the plan's second named risk for this stage, and it is answered by
/// reusing the path rather than by adding a second one.
/// </para>
///
/// <para>
/// <b>Each row is replayed in its own scope, and there is no surrounding transaction.</b> One poisoned row must not
/// abort the batch: inside a single transaction a failed <c>SaveChanges</c> leaves Postgres refusing every later
/// statement (<c>25P02</c>), so the second row's failure would be the transaction's, not its own.
/// </para>
/// </summary>
public interface IFailedStripeWebhookReplayer
{
    /// <summary>
    /// Replays outstanding captures, oldest first, at most <paramref name="maxRows"/> of them.
    /// </summary>
    /// <param name="ids">
    /// The captures to replay, or empty for "every outstanding one". A named id that is already replayed or unknown is
    /// reported rather than silently skipped.
    /// </param>
    Task<FailedStripeWebhookReplayReport> ReplayAsync(
        IReadOnlyCollection<Guid> ids,
        int maxRows,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one replay request did. Per row, because a batch that answers only "7 succeeded" leaves an operator with no
/// way to find the one that did not (decision Q5a: synchronous, hard-capped, per-row report).
/// </summary>
/// <param name="Outstanding">
/// How many captures are still outstanding after this request, ignoring the cap — so an operator can tell "that was
/// all of them" from "press it again".
/// </param>
public sealed record FailedStripeWebhookReplayReport(
    int Attempted,
    int Replayed,
    int StillFailing,
    int Outstanding,
    IReadOnlyList<FailedStripeWebhookReplayResult> Results);

/// <param name="Outcome">See <see cref="FailedStripeWebhookReplayOutcome"/>.</param>
/// <param name="Error">Why it failed again, for a <see cref="FailedStripeWebhookReplayOutcome.Failed"/> row only.</param>
public sealed record FailedStripeWebhookReplayResult(
    Guid Id,
    string? StripeEventId,
    string? EventType,
    FailedStripeWebhookReplayOutcome Outcome,
    string? Error);

public enum FailedStripeWebhookReplayOutcome
{
    /// <summary>Applied. The payment moved, or the event correctly changed nothing.</summary>
    Replayed,

    /// <summary>
    /// The event had already been processed — Stripe's own redelivery, or an earlier replay, got there first. The
    /// capture is closed and the payment was not touched a second time.
    /// </summary>
    AlreadyProcessed,

    /// <summary>It failed again. The capture stays outstanding with the new error and a higher attempt count.</summary>
    Failed,

    /// <summary>The id named in the request is not an outstanding capture.</summary>
    NotFound
}
