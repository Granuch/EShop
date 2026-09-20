using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentEvents;

/// <summary>
/// One payment's timeline (<c>GET /api/v1/payments/{id}/events</c>, Admin panel S11, endpoint #66).
///
/// <para>
/// Nothing recorded what happened to a payment before this stage. <c>PaymentTransaction</c> carries only its current
/// status and one <c>ErrorMessage</c> that each writer overwrites, so a payment that was declined twice, paid on the
/// third card and later refunded looked exactly like one that was refunded straight away — and the operational
/// question about a payment is almost never "what is it now".
/// </para>
///
/// <para>
/// <b>Not paged, and not filtered.</b> A payment's timeline is bounded by its own life: a handful of rows, a few dozen
/// for a payment Stripe retried for days. Paging it would add a contract to maintain for a list that fits on a screen.
/// </para>
/// </summary>
public sealed record GetPaymentEventsQuery(Guid PaymentId) : IRequest<Result<IReadOnlyList<PaymentEventDto>>>;
