using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPayments;

/// <summary>
/// The admin payment list (<c>GET /api/v1/payments</c>, Admin panel S10, endpoint #65). Payment had no cross-user
/// payment list of any kind before this: the only listing endpoint was one user's own payments.
/// <para>It answers the same paged <c>PagedResult&lt;PaymentDto&gt;</c> as <c>GET /api/v1/users/{userId}/payments</c>
/// rather than a second projection — that is the plan's one named requirement for this stage.</para>
/// </summary>
public sealed record GetPaymentsQuery : PaymentFilterQuery, IRequest<Result<PagedResult<PaymentDto>>>
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
}
