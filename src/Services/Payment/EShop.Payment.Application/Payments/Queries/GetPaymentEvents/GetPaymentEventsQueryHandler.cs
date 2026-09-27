using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentEvents;

public sealed class GetPaymentEventsQueryHandler
    : IRequestHandler<GetPaymentEventsQuery, Result<IReadOnlyList<PaymentEventDto>>>
{
    private readonly IPaymentQueryService _paymentQueryService;

    public GetPaymentEventsQueryHandler(IPaymentQueryService paymentQueryService)
    {
        _paymentQueryService = paymentQueryService;
    }

    public async Task<Result<IReadOnlyList<PaymentEventDto>>> Handle(
        GetPaymentEventsQuery request,
        CancellationToken cancellationToken)
    {
        // The existence check is not ceremony. Without it an unknown id answers 200 with [], which is
        // indistinguishable from a payment nothing has happened to yet — and the one thing an operator opening this
        // screen must be able to trust is that an empty timeline means the payment is quiet, not that they pasted the
        // wrong id.
        if (!await _paymentQueryService.PaymentExistsAsync(request.PaymentId, cancellationToken))
        {
            return Result<IReadOnlyList<PaymentEventDto>>.Failure(new Error(
                "PAYMENT_NOT_FOUND",
                "Payment not found."));
        }

        var events = await _paymentQueryService.GetEventsAsync(request.PaymentId, cancellationToken);

        return Result<IReadOnlyList<PaymentEventDto>>.Success(events.Select(e => e.ToDto()).ToList());
    }
}
