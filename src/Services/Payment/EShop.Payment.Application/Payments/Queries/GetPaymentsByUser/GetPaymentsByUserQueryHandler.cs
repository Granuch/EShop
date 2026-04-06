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
        var items = _paymentRepository.Query()
            .Where(x => x.UserId == request.UserId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToList();

        return Result<List<PaymentDto>>.Success(items);
    }
}
