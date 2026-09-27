using EShop.Payment.Domain.Interfaces;

namespace EShop.Payment.Application.Payments.Common;

/// <summary>
/// What one replay request did (Admin panel S11, endpoint #67).
///
/// <para>
/// It exists rather than serializing <see cref="FailedStripeWebhookReplayReport"/> straight out of Domain for the
/// reason <see cref="PaymentStatsDto"/> does: <c>Outcome</c> is an enum, and System.Text.Json writes a bare enum as a
/// <b>number</b>. A per-row report whose rows say <c>2</c> is not a report.
/// </para>
/// </summary>
/// <param name="Outstanding">
/// How many captures are still outstanding after this request, ignoring the cap — so an operator can tell "that was
/// all of them" from "press it again".
/// </param>
public sealed record FailedStripeWebhookReplayDto(
    int Attempted,
    int Replayed,
    int StillFailing,
    int Outstanding,
    IReadOnlyList<FailedStripeWebhookReplayResultDto> Results);

/// <param name="Outcome">
/// <c>Replayed</c>, <c>AlreadyProcessed</c>, <c>Failed</c> or <c>NotFound</c>.
/// </param>
public sealed record FailedStripeWebhookReplayResultDto(
    Guid Id,
    string? StripeEventId,
    string? EventType,
    string Outcome,
    string? Error);

internal static class FailedStripeWebhookReplayMapping
{
    public static FailedStripeWebhookReplayDto ToDto(this FailedStripeWebhookReplayReport report)
        => new(
            report.Attempted,
            report.Replayed,
            report.StillFailing,
            report.Outstanding,
            report.Results
                .Select(r => new FailedStripeWebhookReplayResultDto(
                    r.Id, r.StripeEventId, r.EventType, r.Outcome.ToString(), r.Error))
                .ToList());
}
