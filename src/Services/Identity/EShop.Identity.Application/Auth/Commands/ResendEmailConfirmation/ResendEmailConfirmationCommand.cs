using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Auth.Commands.ResendEmailConfirmation;

/// <summary>
/// <c>POST /api/v1/auth/resend-confirmation</c>: send a fresh email-confirmation link to an account
/// that has not confirmed its address yet.
///
/// <para>
/// Transactional because the handler writes an outbox row; <c>TransactionBehavior</c>'s commit is
/// what persists it. Every outcome answers the same response — see the handler — so the endpoint
/// cannot be used to learn which addresses have accounts, or which of them are unconfirmed.
/// </para>
/// </summary>
public record ResendEmailConfirmationCommand : IRequest<Result<ResendEmailConfirmationResponse>>, ITransactionalCommand
{
    public string Email { get; init; } = string.Empty;
}

public record ResendEmailConfirmationResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
