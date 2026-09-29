using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentStats;

public sealed class GetPaymentStatsQueryHandler : IRequestHandler<GetPaymentStatsQuery, Result<PaymentStatsDto>>
{
    private readonly IPaymentQueryService _paymentQueryService;

    public GetPaymentStatsQueryHandler(IPaymentQueryService paymentQueryService)
    {
        _paymentQueryService = paymentQueryService;
    }

    public async Task<Result<PaymentStatsDto>> Handle(
        GetPaymentStatsQuery request,
        CancellationToken cancellationToken)
    {
        // The validator has already rejected an unknown name, so the fallback here is the omitted case.
        var groupBy = PaymentQueryEnums.ParseGroupBy(request.GroupBy);

        // The window goes through the same PaymentListFilter the list uses, so "in the window" means one thing.
        var window = new PaymentListFilter(
            Currency: request.EffectiveCurrency,
            From: PaymentQueryEnums.AsUtc(request.From),
            To: PaymentQueryEnums.AsUtc(request.To));

        var stats = await _paymentQueryService.GetStatsAsync(window, groupBy, cancellationToken);

        return Result<PaymentStatsDto>.Success(stats.ToDto());
    }
}
