using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentById;

public sealed class GetPaymentByIdQueryHandler : IRequestHandler<GetPaymentByIdQuery, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;

    public GetPaymentByIdQueryHandler(IPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task<Result<PaymentDto>> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        // Not 403: a customer must not learn that someone else's payment exists.
        if (!request.RequesterIsAdmin
            && !string.Equals(payment.UserId, request.RequesterId, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        return Result<PaymentDto>.Success(payment.ToDto());
    }

    private static Result<PaymentDto> NotFound()
        => Result<PaymentDto>.Failure(new Error("PAYMENT_NOT_FOUND", "Payment not found."));
}
