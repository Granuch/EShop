using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

/// <summary>
/// Starts paying an order at Stripe. Payment audit Stage 2 (C2, D4): the request names only the order. What is
/// charged — amount and currency — comes from the payment Payment recorded for it from <c>OrderCreatedEvent</c>,
/// never from the client, which used to choose both.
/// </summary>
/// <param name="RequesterId">The signed-in user's id; a non-admin may only pay their own order.</param>
/// <param name="Email">Optional, for the Stripe customer record.</param>
public sealed record CreatePaymentIntentCommand(
    Guid OrderId,
    string? RequesterId,
    bool RequesterIsAdmin,
    string? Email) : IRequest<Result<CreatePaymentIntentDto>>, ITransactionalCommand;

public sealed record CreatePaymentIntentDto(
    Guid PaymentId,
    string PaymentIntentId,
    string ClientSecret,
    string Status);
