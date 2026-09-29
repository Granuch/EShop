using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.RefundPayment;

public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal? Amount,
    string? Reason) : IRequest<Result<PaymentDto>>, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Payment";

    string? IAuditedCommand.AuditEntityId => PaymentId.ToString();
}
