using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

/// <summary>
/// Starts paying an order at Stripe. Payment audit Stage 2 (C2, D4): the request names only the order. What is
/// charged — amount and currency — comes from the payment Payment recorded for it from <c>OrderCreatedEvent</c>,
/// never from the client, which used to choose both.
/// <para>Deliberately not an <c>ITransactionalCommand</c> (Payment audit D7). Stripe is called with no transaction open, and
/// the result is recorded in one save, guarded by the row version. See the handler.</para>
/// </summary>
/// <param name="RequesterId">The signed-in user's id; a non-admin may only pay their own order.</param>
/// <param name="Email">
/// Optional, for the Stripe customer record. It is personal data, so <c>LoggingBehavior</c> redacts it (Payment audit
/// Stage 12). "Email" is not on the behavior's list of secret names, so it used to be logged in clear text.
/// </param>
public sealed record CreatePaymentIntentCommand(
    Guid OrderId,
    string? RequesterId,
    bool RequesterIsAdmin,
    [property: SensitiveData] string? Email) : IRequest<Result<CreatePaymentIntentDto>>;

public sealed record CreatePaymentIntentDto(
    Guid PaymentId,
    string PaymentIntentId,
    string ClientSecret,
    string Status);
