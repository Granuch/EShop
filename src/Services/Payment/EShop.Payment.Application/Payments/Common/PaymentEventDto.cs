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
/// <remarks>
/// <b>There is no actor field, and that is a finding rather than an omission.</b> Ordering's status history reports
/// one for free, because <c>BaseDbContext.SetAuditFields</c> stamps <c>CreatedBy</c> from <c>ICurrentUserContext</c>
/// — which Payment alone of the six services does not register. Adding the field here would emit <c>null</c> for
/// every row ever written. See <see cref="PaymentEvent"/>.
/// </remarks>
public sealed record PaymentEventDto(
    Guid Id,
    string Kind,
    string? StripeEventId,
    string? FromStatus,
    string ToStatus,
    string Detail,
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
            paymentEvent.OccurredAt);
}
