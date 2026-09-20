using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Application.Payments.Common;

/// <summary>
/// One row of a payment's timeline (Admin panel S11, endpoint #66).
/// </summary>
/// <param name="Kind">
/// <c>Transition</c> or <c>Webhook</c>. Serialized as its name for the same reason
/// <see cref="PaymentDto.Status"/> is: System.Text.Json writes a bare enum as a <b>number</b>, so one payment endpoint
/// would report <c>"SUCCESS"</c> and its timeline <c>2</c> for the same fact.
/// </param>
/// <param name="FromStatus">
/// Null on the row a payment's creation writes. Equal to <paramref name="ToStatus"/> when the event changed something
/// other than the status — a declined card, a refund note, a late webhook that was correctly ignored.
/// </param>
/// <param name="ActorId">
/// Who did it: the operator's user id for an admin action, <c>"system"</c> for a consumer, a webhook or the outbox
/// processor. It is <see cref="PaymentEvent"/>'s <c>CreatedBy</c>, which <c>BaseDbContext.SetAuditFields</c> stamps
/// from <c>ICurrentUserContext</c>, so no domain method had to learn about actors.
/// <para>Null is possible and means the context could not name anyone — an unauthenticated HTTP request, which no
/// endpoint that moves a payment allows. Rows written before <c>PaymentEventAuditFields</c> also read null, because
/// the column did not exist when they were inserted.</para>
/// </param>
public sealed record PaymentEventDto(
    Guid Id,
    string Kind,
    string? StripeEventId,
    string? FromStatus,
    string ToStatus,
    string Detail,
    string? ActorId,
    DateTime OccurredAt);

internal static class PaymentEventMapping
{
    public static PaymentEventDto ToDto(this PaymentEvent paymentEvent)
        => new(
            paymentEvent.Id,
            paymentEvent.Kind.ToString(),
            paymentEvent.StripeEventId,
            paymentEvent.FromStatus?.ToString().ToUpperInvariant(),
            paymentEvent.ToStatus.ToString().ToUpperInvariant(),
            paymentEvent.Detail,
            paymentEvent.CreatedBy,
            paymentEvent.OccurredAt);
}
