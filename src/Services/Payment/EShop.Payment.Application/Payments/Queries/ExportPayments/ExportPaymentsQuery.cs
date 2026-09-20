using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.ExportPayments;

/// <summary>
/// The accounting export (<c>GET /api/v1/payments/export</c>, Admin panel S10, endpoint #69). Exactly the filters of
/// the admin list, with no paging: an export the caller has to page through is not an export.
/// <para>The rows come back as <c>PaymentDto</c>; the API layer turns them into CSV. Where a comma goes in a file is
/// a presentation decision, and keeping it out of Application is what lets the same query answer a second format
/// later without a second handler.</para>
/// </summary>
public sealed record ExportPaymentsQuery : PaymentFilterQuery, IRequest<Result<IReadOnlyList<PaymentDto>>>
{
    /// <summary>
    /// The most rows one export may return.
    ///
    /// <para><b>Exceeding it is an error, not a truncation.</b> A silently shortened accounting export is a wrong
    /// answer that looks exactly like a right one — the operator reconciles against it and the missing rows are never
    /// noticed. Refusing tells them to narrow the window, which is the only safe thing a bounded export can do.
    /// Risk A8 asks for an explicit cap here, mirroring Notification's <c>MaxReplayPerRequest = 1000</c>.</para>
    /// </summary>
    public const int MaxRows = 10_000;
}
