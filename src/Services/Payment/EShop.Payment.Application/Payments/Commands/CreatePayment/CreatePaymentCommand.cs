using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePayment;

public sealed record CreatePaymentCommand(
    Guid OrderId,
    string UserId,
    decimal Amount,
    string? Currency,
    string? PaymentMethod) : IRequest<Result<PaymentDto>>, ITransactionalCommand;
