using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPayments;

public sealed class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, Result<PagedResult<PaymentDto>>>
{
    private readonly IPaymentQueryService _paymentQueryService;

    public GetPaymentsQueryHandler(IPaymentQueryService paymentQueryService)
    {
        _paymentQueryService = paymentQueryService;
    }

    public async Task<Result<PagedResult<PaymentDto>>> Handle(
        GetPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        var (payments, totalCount) = await _paymentQueryService.GetPageAsync(
            request.ToFilter(),
            pageNumber,
            pageSize,
            cancellationToken);

        return Result<PagedResult<PaymentDto>>.Success(PagedResult<PaymentDto>.Create(
            payments.Select(p => p.ToDto()).ToList(),
            pageNumber,
            pageSize,
            totalCount));
    }
}
