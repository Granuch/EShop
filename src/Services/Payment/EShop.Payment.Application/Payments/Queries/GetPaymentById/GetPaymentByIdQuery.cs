using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentById;

/// <summary>
/// One payment, for its owner or an admin. Payment audit Stage 12: a customer asking for another customer's payment is
/// told it is not found, as <c>/create-intent</c> does. The endpoint used to load the payment first and answer 403, which
/// confirmed that the id existed.
/// </summary>
public sealed record GetPaymentByIdQuery(Guid PaymentId, string? RequesterId, bool RequesterIsAdmin) : IRequest<Result<PaymentDto>>;
