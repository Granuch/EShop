using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Command to request password reset.
///
/// It used to be marked "not transactional — this is a read-only lookup + token generation
/// flow", which was wrong: the handler writes a <c>PasswordResetRequestedIntegrationEvent</c> to
/// the outbox and then called <c>SaveChangesAsync</c> itself. That is the same shape as BUG-07 —
/// a handler driving the unit of work outside the behavior that is supposed to own it — and it
/// meant the outbox write was untransacted.
/// </summary>
public record ForgotPasswordCommand : IRequest<Result<ForgotPasswordResponse>>, ITransactionalCommand
{
    public string Email { get; init; } = string.Empty;
}

public record ForgotPasswordResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
