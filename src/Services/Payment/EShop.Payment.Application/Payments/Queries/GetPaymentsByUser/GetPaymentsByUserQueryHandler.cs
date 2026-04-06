using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;

public sealed class GetPaymentsByUserQueryHandler : IRequestHandler<GetPaymentsByUserQuery, Result<List<PaymentDto>>>
{
    private readonly IPaymentRepository _paymentRepository;

    public GetPaymentsByUserQueryHandler(IPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task<Result<List<PaymentDto>>> Handle(GetPaymentsByUserQuery request, CancellationToken cancellationToken)
    {
        var payments = await _paymentRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var items = payments
            .Select(x => x.ToDto())
            .ToList();

        return Result<List<PaymentDto>>.Success(items);
    }
}
