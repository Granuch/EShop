using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

public sealed record CreatePaymentIntentCommand(
    Guid OrderId,
    string UserId,
    decimal Amount,
    string? Currency,
    string? Email) : IRequest<Result<CreatePaymentIntentDto>>, ITransactionalCommand;

public sealed record CreatePaymentIntentDto(
    Guid PaymentId,
    string PaymentIntentId,
    string ClientSecret,
    string Status);
