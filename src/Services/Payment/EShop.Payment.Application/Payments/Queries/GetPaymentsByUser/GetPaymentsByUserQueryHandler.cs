using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;

public sealed class GetPaymentsByUserQueryHandler : IRequestHandler<GetPaymentsByUserQuery, Result<PagedResult<PaymentDto>>>
{
    private readonly IPaymentRepository _paymentRepository;

    public GetPaymentsByUserQueryHandler(IPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task<Result<PagedResult<PaymentDto>>> Handle(GetPaymentsByUserQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        var (payments, totalCount) = await _paymentRepository.GetPageByUserIdAsync(
            request.UserId,
            pageNumber,
            pageSize,
            cancellationToken);

        return Result<PagedResult<PaymentDto>>.Success(PagedResult<PaymentDto>.Create(
            payments.Select(x => x.ToDto()).ToList(),
            pageNumber,
            pageSize,
            totalCount));
    }
}
