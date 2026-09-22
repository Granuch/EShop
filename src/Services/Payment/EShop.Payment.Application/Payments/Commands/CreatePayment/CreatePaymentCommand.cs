using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePayment;

/// <summary>
/// An admin settles an order's recorded Pending payment through the simulator. Payment audit Stage 3 (H4, D2): this
/// was a customer endpoint that created a payment with whatever user, amount, currency and method the request
/// named. The payment now comes from Payment's record of the order, so only the order is named.
/// </summary>
public sealed record CreatePaymentCommand(Guid OrderId) : IRequest<Result<PaymentDto>>, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Payment";

    string? IAuditedCommand.AuditEntityId => null;

    string? IAuditedCommand.AuditEntityIdFromResult(object? value) => (value as PaymentDto)?.Id.ToString();
}
