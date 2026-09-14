using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;

/// <summary>
/// One page of a user's payments, newest first (Payment audit S10: M7, D10). This is the shape of Ordering's per-user
/// order list. It used to return every payment the user ever had, as a bare array. The page values are nullable, so
/// that under <c>[AsParameters]</c> they stay optional query parameters.
/// </summary>
public sealed record GetPaymentsByUserQuery : IRequest<Result<PagedResult<PaymentDto>>>
{
    public string UserId { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
}
