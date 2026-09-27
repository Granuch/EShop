using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.ExportPayments;

public sealed class ExportPaymentsQueryHandler
    : IRequestHandler<ExportPaymentsQuery, Result<IReadOnlyList<PaymentDto>>>
{
    private readonly IPaymentQueryService _paymentQueryService;

    public ExportPaymentsQueryHandler(IPaymentQueryService paymentQueryService)
    {
        _paymentQueryService = paymentQueryService;
    }

    public async Task<Result<IReadOnlyList<PaymentDto>>> Handle(
        ExportPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.ToFilter();

        // Counted before anything is fetched. The alternative — fetch MaxRows + 1 and check — reads the rows this
        // branch exists to avoid reading.
        var matching = await _paymentQueryService.CountAsync(filter, cancellationToken);
        if (matching > ExportPaymentsQuery.MaxRows)
        {
            return Result<IReadOnlyList<PaymentDto>>.Failure(new Error(
                "EXPORT_TOO_LARGE",
                $"{matching} payments match; an export is limited to {ExportPaymentsQuery.MaxRows}. "
                + "Narrow the date range or the filters."));
        }

        var payments = await _paymentQueryService.ListAsync(filter, ExportPaymentsQuery.MaxRows, cancellationToken);

        return Result<IReadOnlyList<PaymentDto>>.Success(payments.Select(p => p.ToDto()).ToList());
    }
}
