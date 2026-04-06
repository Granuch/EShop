using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using MediatR;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;

public sealed record GetPaymentsByUserQuery(string UserId) : IRequest<Result<List<PaymentDto>>>;
